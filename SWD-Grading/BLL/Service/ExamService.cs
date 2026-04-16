using AutoMapper;
using BLL.Exceptions;
using BLL.Interface;
using BLL.Model.Request.Exam;
using BLL.Model.Response;
using BLL.Model.Response.Exam;
using DAL.Interface;
using DocumentFormat.OpenXml.Packaging;
using Microsoft.AspNetCore.Http;
using Model.Entity;
using OfficeOpenXml.Style;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;
using DocumentFormat.OpenXml.Spreadsheet;
using BLL.Model.Request.Student;
using Model.Enums;
using BLL.Model.Response.ExamQuestion;
using BLL.Model.Response.Rubric;
using Amazon.Runtime.Telemetry.Tracing;
using Grpc.Core;
using BLL.Model.Request.Grade;
using BLL.Model.Response.Grade;
using Amazon;
using Amazon.S3;
using Amazon.S3.Model;
using Model.Configuration;
using Microsoft.Extensions.Configuration;
using OfficeOpenXml;
using Microsoft.EntityFrameworkCore;
using DocumentFormat.OpenXml.Office2016.Excel;
using System.Globalization;

namespace BLL.Service
{
	public class ExamService : IExamService
	{
		private readonly IUnitOfWork _unitOfWork;
		private readonly IMapper _mapper;
		private readonly IGradeService _gradeService;
		private readonly IS3Service _s3Service;
		private readonly IAmazonS3 _s3Client;
		private readonly AwsConfiguration _awsConfig;
		public ExamService(IUnitOfWork unitOfWork, IMapper mapper, IGradeService gradeService, IS3Service s3Service, IAmazonS3 s3Client, IConfiguration configuration)
		{
			_unitOfWork = unitOfWork;
			_mapper = mapper;
			_gradeService = gradeService;
			_s3Service = s3Service;
			_s3Client = s3Client;
			_awsConfig = new AwsConfiguration();
			configuration.GetSection("AWS").Bind(_awsConfig);
		}

		public async Task<ExamResponse> CreateExam(CreateExamRequest request)
		{
			bool isDuplicatedCode = await _unitOfWork.ExamRepository.GetByExamCodeAsync(request.ExamCode) == null ? false : true;
			if (isDuplicatedCode)
				throw new AppException("Duplicated exam code", 400);
			Exam exam = _mapper.Map<Exam>(request);
			await _unitOfWork.ExamRepository.AddAsync(exam);
			await _unitOfWork.SaveChangesAsync();
			
			var response = _mapper.Map<ExamResponse>(exam);
			response.ExamPaper = _s3Service.GetPresignedUrlFromFullUrl(response.ExamPaper);
			return response;
		}

		public async Task<bool> DeleteAsync(long id)
		{
			var exam = await _unitOfWork.ExamRepository.GetByIdAsync(id);
			if (exam == null)
				throw new AppException("Exam not found", 404);
			await _unitOfWork.ExamRepository.RemoveAsync(exam);
			await _unitOfWork.SaveChangesAsync();
			return true;
		}

		public async Task<PagingResponse<ExamResponse>> GetAllAsync(ExamFilter filter)
		{
			var filters = new List<Expression<Func<Exam, bool>>>();
			if (filter.Page <= 0)
				throw new AppException("Page number must be greater than or equal to 1", 400);
			if (filter.Size < 0)
				throw new AppException("Size must not be negative", 400);
			var skip = (filter.Page - 1) * filter.Size;
			var totalItems = await _unitOfWork.ExamRepository.CountAsync(filters);
			Func<IQueryable<Exam>, IOrderedQueryable<Exam>> orderBy = q => q.OrderByDescending(o => o.CreatedAt);
			var data = await _unitOfWork.ExamRepository.GetPagedAsync<Exam>(
				skip, filter.Size, filters, orderBy, null, null, asNoTracking: true);
			var responses = _mapper.Map<IEnumerable<ExamResponse>>(data.ToList());
			foreach (var res in responses)
			{
				res.ExamPaper = _s3Service.GetPresignedUrlFromFullUrl(res.ExamPaper);
			}
			return new()
			{
				Result = responses,
				Page = filter.Page,
				Size = filter.Size,
				TotalItems = totalItems,
				TotalPages = (int)Math.Ceiling((double)totalItems / filter.Size)
			};
		}

		public async Task<ExamResponse?> GetByIdAsync(long id)
		{
			var exam = await _unitOfWork.ExamRepository.GetByIdAsync(id);
			if (exam == null) throw new AppException("Exam not found", 404);
			var response = _mapper.Map<ExamResponse>(exam);
			response.ExamPaper = _s3Service.GetPresignedUrlFromFullUrl(response.ExamPaper);
			return response;
		}

		public async Task ParseDetailExcel(long examId, IFormFile file)
		{
			// 0. Lấy exam để có examCode
			var exam = await _unitOfWork.ExamRepository.GetByIdAsync(examId);
			if (exam == null)
			{
				throw new Exception("Exam not found");
			}

			if (file == null || file.Length == 0)
				throw new AppException("Invalid uploaded file", 400);

			var examCode = exam.ExamCode ?? "NO_CODE";

			using var ms = new MemoryStream();
			try
			{
				await file.CopyToAsync(ms);
			}
			catch (Exception)
			{
				throw new AppException("Failed to read uploaded file", 500);
			}

			// 2. Đặt Position về 0 để đọc Excel
			ms.Position = 0;
			using var doc = SpreadsheetDocument.Open(ms, false);

			var wb = doc.WorkbookPart ?? throw new AppException("WorkbookPart missing", 500);
			var sheets = wb.Workbook?.Sheets;
			if (sheets == null || sheets.Count() == 0)
				throw new AppException("No sheets found in Excel", 400);
			var sheet = sheets.GetFirstChild<Sheet>();
			if (sheet == null)
				throw new AppException("Sheet is empty", 400);
			var wsPart = (WorksheetPart)wb.GetPartById(sheet.Id!);
			if (wsPart == null)
				throw new AppException("WorksheetPart not found", 500);
			var sheetData = wsPart.Worksheet.GetFirstChild<SheetData>()!;
			if (sheetData == null)
				throw new AppException("SheetData missing", 500);
			var rows = sheetData.Elements<Row>().ToList();
			if (rows.Count < 3)
				throw new AppException("Template missing header rows", 400);
			//--------------------------------------------------------------------
			// 1) READ PART NAME + DESCRIPTIONS → ExamQuestion + Rubric
			//--------------------------------------------------------------------
			Row partRow = rows[0];   // Part 1 / Part 2 / Part 3...
			Row descRow = rows[1];   // Description
			Row scoreRow = rows[2];  // MaxScore của từng rubric

			int maxPartCol = GetMaxColumnIndex(partRow);
			int maxDescCol = GetMaxColumnIndex(descRow);
			int maxScoreCol = GetMaxColumnIndex(scoreRow);
			int trueMaxCol = Math.Max(maxPartCol, Math.Max(maxDescCol, maxScoreCol));
			int colCount = trueMaxCol;

			List<ExamQuestion> questions = new();
			List<Rubric> rubrics = new();

			int questionNumber = 1;
			ExamQuestion? currentQuestion = null;
			decimal currentPartTotal = 0;

			for (int c = 3; c <= colCount; c++)
			{
				string partName = GetCellValue(doc, GetOrCreateCell(wsPart, partRow, c));
				string desc = GetCellValue(doc, GetOrCreateCell(wsPart, descRow, c));
				string scoreStr = GetCellValue(doc, GetOrCreateCell(wsPart, scoreRow, c));

				if (partName.Trim().Equals("Total", StringComparison.OrdinalIgnoreCase) || partName.Trim().Equals("Comment", StringComparison.OrdinalIgnoreCase))
					continue;
				if (desc.Trim().Equals("Total", StringComparison.OrdinalIgnoreCase) || desc.Trim().Equals("Comment", StringComparison.OrdinalIgnoreCase))
					continue;

				decimal rubricMaxScore = 0;
				decimal.TryParse(scoreStr, NumberStyles.Any, CultureInfo.InvariantCulture, out rubricMaxScore);
				
				// Nếu gặp "Part 1", "Part 2", "Part 3" hoặc "Question 1"...
				if (!string.IsNullOrWhiteSpace(partName))
				{
					// Nếu đang ở part trước → cập nhật tổng điểm
					if (currentQuestion != null)
					{
						currentQuestion.MaxScore = currentPartTotal;
						if (currentQuestion.Id > 0) {
							await _unitOfWork.ExamQuestionRepository.UpdateAsync(currentQuestion);
						}
					}

					// Reset
					currentPartTotal = 0;

					// Find if question already exists in DB (e.g. from ParseDocxQuestions)
					var existingQuestions = (await _unitOfWork.ExamQuestionRepository.GetQuestionByExamId(examId)).ToList();
					var existingQ = existingQuestions.FirstOrDefault(q => q.QuestionText.Trim().ToLower() == partName.Trim().ToLower());

					if (existingQ != null)
					{
						currentQuestion = existingQ;
					}
					else
					{
						currentQuestion = new ExamQuestion
						{
							ExamId = examId,
							QuestionNumber = questionNumber++,
							QuestionText = partName,
							MaxScore = 0 // sẽ cập nhật sau
						};
						questions.Add(currentQuestion);
					}
				}

				// Thêm Rubric thuộc part hiện tại
				if (currentQuestion != null && !string.IsNullOrWhiteSpace(desc))
				{
					var newRubric = new Rubric
					{
						Criterion = desc,
						MaxScore = rubricMaxScore,
						OrderIndex = rubrics.Count + 1
					};

					if (currentQuestion.Id > 0) {
						newRubric.ExamQuestionId = currentQuestion.Id;
					} else {
						newRubric.ExamQuestion = currentQuestion;
					}

					rubrics.Add(newRubric);
					currentPartTotal += rubricMaxScore; // cộng dồn điểm cho Part
				}
			}

			// Sau vòng lặp, cập nhật Part cuối cùng
			if (currentQuestion != null)
			{
				currentQuestion.MaxScore = currentPartTotal;
				if (currentQuestion.Id > 0) {
					await _unitOfWork.ExamQuestionRepository.UpdateAsync(currentQuestion);
				}
			}

			//--------------------------------------------------------------------
			// 2) READ STUDENT + EXAMSTUDENT
			//--------------------------------------------------------------------
			List<Student> students = new();
			List<ExamStudent> examStudents = new();

			for (int r = 3; r < rows.Count; r++)
			{
				var cells = rows[r].Elements<Cell>().ToList();
				if (cells.Count < 3) continue;

				string solution = GetCellValue(doc, cells[1]); // StudentCode
				string markerCode = GetCellValue(doc, cells[2]); // TeacherCode

				if (string.IsNullOrWhiteSpace(solution))
					continue;

				//--------------------------------------------------------------
				// Create Student
				//--------------------------------------------------------------
				(bool existed, Student student) result = await GetOrCreateStudentAsync(solution);
				if (!result.existed)
					students.Add(result.student);

				//--------------------------------------------------------------
				// Get or create Teacher
				//--------------------------------------------------------------
				var teacher = await GetOrCreateTeacherAsync(markerCode);

				//--------------------------------------------------------------
				// Add ExamStudent
				//--------------------------------------------------------------
				(bool existed, ExamStudent? es) resultExamStudent = await GetOrCreateExamStudentAsync(examId, result.student, result.existed);
				if (!resultExamStudent.existed)
				{
					examStudents.Add(new ExamStudent
					{
						ExamId = examId,
						Student = result.student,
						TeacherId = teacher.Id,
						Status = ExamStudentStatus.NOT_FOUND, // default
						Note = null
					});
				}

			}
			ms.Position = 0; // RẤT QUAN TRỌNG: reset về đầu trước khi upload

			var s3Path = $"{examCode}/original-file"; // examCode/original-file
			var originalFileUrl = await _s3Service.UploadExcelFileAsync(ms, file.FileName, s3Path);

			// Nếu muốn lưu URL lại trong bảng Exam:
			bool isOriginalChanged = exam.OriginalExcel != originalFileUrl;
			exam.OriginalExcel = originalFileUrl;
			
			// (exam đang được tracking bởi DbContext, chỉ cần gán là đủ)
			//--------------------------------------------------------------------
			// 3) SAVE ALL → chỉ SaveChanges 1 lần cho hiệu suất
			//--------------------------------------------------------------------
			bool saved = false;
			if (students.Count > 0)
			{
				await _unitOfWork.StudentRepository.AddRangeAsync(students);
				saved = true;
			}

			if (examStudents.Count > 0)
			{
				await _unitOfWork.ExamStudentRepository.AddRangeAsync(examStudents);
				saved = true;
			}
			if (questions.Count > 0)
			{
				await _unitOfWork.ExamQuestionRepository.AddRangeAsync(questions);
				saved = true;
			}

			if (rubrics.Count > 0)
			{
				await _unitOfWork.RubricRepository.AddRangeAsync(rubrics);
				saved = true;
			}

			if (isOriginalChanged || saved)
			{
				await _unitOfWork.SaveChangesAsync();
				if (examStudents.Count > 0)
					await CreateGradeForExamStudent(examId, examStudents);
			}

		}

		private async Task<User> GetOrCreateTeacherAsync(string teacherCode)
		{
			// Try get existing
			var teacher = await _unitOfWork.UserRepository.GetByTeacherCodeAsync(teacherCode);
			if (teacher != null)
				return teacher;

			// Create new
			teacher = new User
			{
				Username = teacherCode,
				TeacherCode = teacherCode,
				PasswordHash = BCrypt.Net.BCrypt.HashPassword("123456"),
				IsActive = true,
				Role = UserRole.TEACHER
			};

			await _unitOfWork.UserRepository.AddAsync(teacher);

			// VERY IMPORTANT: SAVE so EF generates Teacher.Id
			await _unitOfWork.SaveChangesAsync();

			return teacher;
		}

		private async Task<(bool existed, Student s)> GetOrCreateStudentAsync(string solution)
		{
			// Try get existing
			var student = await _unitOfWork.StudentRepository.GetByStudentCodeAsync(solution);
			if (student != null)
				return (true, student);

			// Create new
			student = new Student
			{
				StudentCode = solution,
				FullName = solution,
				Email = $"{solution}@fpt.edu.vn"
			};

			return (false, student);
		}

		private async Task<(bool existed, ExamStudent? es)> GetOrCreateExamStudentAsync(long examId, Student student, bool isExsitedStudent)
		{
			// Try get existing
			if (isExsitedStudent)
			{
				var examStudent = await _unitOfWork.ExamStudentRepository.GetByExamAndStudentAsync(examId, student.Id);
				if (examStudent != null)
					return (true, examStudent);
			}
			return (false, null);
		}

		private string GetCellValue(SpreadsheetDocument doc, Cell cell)
		{
			if (cell.CellValue == null)
				return "";

			string value = cell.CellValue.InnerText;

			if (cell.DataType != null && cell.DataType.Value == CellValues.SharedString)
			{
				var table = doc.WorkbookPart!.SharedStringTablePart!.SharedStringTable;
				return table.ChildElements[int.Parse(value)].InnerText;
			}

			return value;
		}

		public async Task<ExamResponse?> UpdateAsync(long id, UpdateExamRequest request)
		{
			var exam = await _unitOfWork.ExamRepository.GetByIdAsync(id);
			if (exam == null)
				throw new AppException("Exam not found", 404);
			_mapper.Map(request, exam);
			await _unitOfWork.ExamRepository.UpdateAsync(exam);
			await _unitOfWork.SaveChangesAsync();
			return _mapper.Map<ExamResponse>(exam);
		}

		public async Task<ExamResponse?> GetQuestionByExamId(long id)
		{
			var exam = await _unitOfWork.ExamRepository.GetByIdAsync(id);
			if (exam == null)
				throw new AppException("Exam not found", 404);
			ExamResponse response = _mapper.Map<ExamResponse>(exam);
			IEnumerable<ExamQuestion> questions = await _unitOfWork.ExamQuestionRepository.GetQuestionByExamId(id);
			List<ExamQuestionResponse> questionResponses = _mapper.Map<List<ExamQuestionResponse>>(questions.ToList());
			foreach (var question in questionResponses)
			{
				IEnumerable<Rubric> rubrics = await _unitOfWork.RubricRepository.GetRubricByQuestionId(question.Id);
				question.Rubrics = _mapper.Map<List<RubricResponse>>(rubrics.ToList());
			}
			response.Questions = questionResponses;
			return response;
		}

		private async Task CreateGradeForExamStudent(long examId, List<ExamStudent> students)
		{
			var requests = new List<AddGradeRangeRequest>();
			foreach (var student in students)
			{
				requests.Add(new AddGradeRangeRequest
				{
					ExamStudentId = student.Id,
					TotalScore = 0,
					Comment = "",
					GradedAt = DateTime.UtcNow,
					GradedBy = null,
					Attempt = 1,
					Status = GradeStatus.CREATED
				});
			}

			await _gradeService.CreateRange(examId, requests);
		}

		public async Task<GradeExportResponse> ExportGradeExcel(int userId, UserRole role, long id)
		{
			if (userId <= 0)
				throw new AppException("Invalid userId", 400);

			if (!Enum.IsDefined(typeof(UserRole), role))
				throw new AppException("Invalid user role", 400);

			if (id <= 0)
				throw new AppException("Invalid exam id", 400);
			// 1. Load file template từ S3
			var exam = await _unitOfWork.ExamRepository.GetByIdAsync(id);
			if (exam == null)
				throw new AppException("Exam not found", 404);
			if (exam.OriginalExcel == null)
				throw new AppException("You have not imported file", 400);
			var key = GetS3KeyFromUrl(exam.OriginalExcel);
			if (string.IsNullOrWhiteSpace(key))
				throw new AppException("Invalid S3 key extracted from OriginalExcel", 500);
			MemoryStream original;
			try
			{
				original = await DownloadFromS3Async(key);
			}
			catch (AmazonS3Exception ex)
			{
				throw new AppException($"Failed to download original Excel template from S3: {ex.Message}", 500);
			}
			if (original == null)
				throw new AppException("Failed to download original Excel template", 500);
			if (!original.CanRead)
				throw new AppException("Downloaded file stream is unreadable", 500);
			// Copy stream để chỉnh sửa bằng OpenXML
			var ms = new MemoryStream();
			try
			{
				original.CopyTo(ms);
			}
			catch
			{
				throw new AppException("Failed to copy Excel template stream", 500);
			}
			ms.Position = 0;

			var settings = new OpenSettings { AutoSave = true };

			using (var doc = SpreadsheetDocument.Open(ms, true, settings))
			{
				var wbPart = doc.WorkbookPart!;
				if (wbPart == null)
					throw new AppException("WorkbookPart is missing in Excel file", 500);
				var allSheets = wbPart.Workbook.Sheets.Cast<Sheet>().ToList();
				var sheet = allSheets.FirstOrDefault(s =>
				{
					var n = (s.Name?.Value ?? "").Trim();
					return n.Contains("Marking", StringComparison.OrdinalIgnoreCase)
						|| n.Contains("Mark", StringComparison.OrdinalIgnoreCase)
						|| n.Contains("Barem", StringComparison.OrdinalIgnoreCase)
						|| n.Contains("Grade", StringComparison.OrdinalIgnoreCase);
				}) ?? allSheets.FirstOrDefault();
				if (sheet == null)
					throw new AppException("Sheet 'Marking' not found in Excel template", 400);
				var wsPart = (WorksheetPart)wbPart.GetPartById(sheet.Id!);
				if (wsPart == null)
					throw new AppException("WorksheetPart not found for 'Marking' sheet", 500);
				var ws = wsPart.Worksheet;
				if (ws == null)
					throw new AppException("Worksheet XML is missing", 500);

				var sheetData = ws.GetFirstChild<SheetData>();
				if (sheetData == null)
					throw new AppException("SheetData not found in worksheet", 500);
				var rows = sheetData.Elements<Row>().ToList();
				if (rows.Count < 2)
					throw new AppException("Template is missing required header rows", 400);
				//---------------------------------------------------------
				// 3. Mapping rubric row
				//---------------------------------------------------------
				int colD = 3;
				Row partRow = rows[0];
				Row rubricRow = rows[1];
				Row scoreRow = rows[2];
				var rubricMap = new Dictionary<string, int>();
				int totalCol = -1;
				int commentCol = -1;
				
				int maxSearchCol = Math.Max(GetMaxColumnIndex(partRow), Math.Max(GetMaxColumnIndex(rubricRow), GetMaxColumnIndex(scoreRow)));

				for (int col = colD; col <= maxSearchCol; col++)
				{
					var pName = GetCellValue(doc, GetOrCreateCell(wsPart, partRow, col));
					var name = GetCellValue(doc, GetOrCreateCell(wsPart, rubricRow, col));
					
					bool isTotal = pName.Trim().Equals("Total", StringComparison.OrdinalIgnoreCase) || name.Trim().Equals("Total", StringComparison.OrdinalIgnoreCase);
					bool isComment = pName.Trim().Equals("Comment", StringComparison.OrdinalIgnoreCase) || name.Trim().Equals("Comment", StringComparison.OrdinalIgnoreCase);
					
					if (isTotal) totalCol = col;
					else if (isComment) commentCol = col;
					else if (!string.IsNullOrWhiteSpace(name))
						rubricMap[name.Trim()] = col;
				}

				//---------------------------------------------------------
				// 4. Load student scores
				//---------------------------------------------------------
				var examStudents = new List<ExamStudent>();
				if (role.Equals(UserRole.TEACHER))
					examStudents = await _unitOfWork.ExamStudentRepository.GetExamStudentByExamId(userId, id);
				else
					examStudents = await _unitOfWork.ExamStudentRepository.GetExamStudentByExamId(id);

				if (examStudents == null)
					throw new AppException("Failed to load exam students", 500);

				if (examStudents.Count == 0)
					throw new AppException("No students found for this exam", 404);
				int rowStart = 3;

				if (role.Equals(UserRole.TEACHER))
				{
					var first = examStudents[0];
					if (first.Teacher == null)
						throw new AppException("Missing teacher information", 500);

					if (string.IsNullOrWhiteSpace(first.Teacher.TeacherCode))
						throw new AppException("Teacher code is missing", 500);

					HideOtherTeacherRows(doc, wsPart, first.Teacher.TeacherCode);
				}

				rows = ws.GetFirstChild<SheetData>()!.Elements<Row>().ToList();
				//---------------------------------------------------------
				// 6. Fill scores — GIỮ CÔNG THỨC
				//---------------------------------------------------------
				for (int i = 0; i < examStudents.Count; i++)
				{
					var stud = examStudents[i];
					if (stud.Student == null)
						throw new AppException("Student data is missing", 500);
					if (string.IsNullOrWhiteSpace(stud.Student.StudentCode))
						throw new AppException("Student code is missing", 500);
					var grade = stud.Grades
						.Where(g => g.Status == GradeStatus.GRADED)
						.OrderByDescending(g => g.Attempt)
						.FirstOrDefault();
					if (grade == null) continue;
					if (grade.Details == null)
						throw new AppException("Grade details missing for a graded student", 500);
					var row = FindRowByStudentCode(doc, wsPart, rows, stud.Student.StudentCode);
					if (row == null)
						throw new AppException($"Row not found for student code {stud.Student.StudentCode}", 400);

					foreach (var detail in grade.Details)
					{
						string cri = detail.Rubric.Criterion.Trim();
						decimal score = detail.Score;
						if (!rubricMap.TryGetValue(cri, out int col)) continue;

						var cell = GetOrCreateCell(wsPart, row, col);

						// ❗ Nếu ô có công thức → không ghi đè
						if (cell.CellFormula != null)
						{
							Console.WriteLine($"[DEBUG] Skip formula cell: {cell.CellReference}");
							continue;
						}

						// Ghi giá trị
						cell.CellValue = new CellValue(score.ToString(CultureInfo.InvariantCulture));
						cell.DataType = CellValues.Number;
					}

					if (totalCol != -1)
					{
						var totalCell = GetOrCreateCell(wsPart, row, totalCol);
						if (totalCell.CellFormula == null)
						{
							totalCell.CellValue = new CellValue(grade.TotalScore.ToString(CultureInfo.InvariantCulture));
							totalCell.DataType = CellValues.Number;
						}
					}

					if (commentCol != -1)
					{
						var commentCell = GetOrCreateCell(wsPart, row, commentCol);
						commentCell.CellValue = new CellValue(grade.Comment ?? "");
						commentCell.DataType = CellValues.String;
					}
				}
				// ❗ BẢO TOÀN CÔNG THỨC → KHÔNG XOÁ calcChain
				// KHÔNG ĐỤNG TỚI calcChain.xml
				var calcProps = wbPart.Workbook.CalculationProperties;

				if (calcProps == null)
				{
					calcProps = new CalculationProperties()
					{
						CalculationId = 0,
						ForceFullCalculation = true,
						FullCalculationOnLoad = true
					};
					wbPart.Workbook.Append(calcProps);
				}
				else
				{
					calcProps.ForceFullCalculation = true;
					calcProps.FullCalculationOnLoad = true;
				}

				ws.Save();
				wbPart.Workbook.Save();
			}

			//---------------------------------------------------------
			// 7. Upload file lên S3
			//---------------------------------------------------------
			ms.Position = 0;
			string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
			string fileName = $"GradeExport_[{exam.ExamCode}]_[{timestamp}].xlsx";
			string uploadPath = $"{exam.ExamCode}/grade-export";
			string url;
			try
			{
				url = await _s3Service.UploadExcelFileAsync(ms, fileName, uploadPath);
			}
			catch
			{
				throw new AppException("Failed to upload exported file to S3", 500);
			}
			if (string.IsNullOrWhiteSpace(url))
				throw new AppException("S3 did not return a valid URL", 500);
			//---------------------------------------------------------
			// 8. Lưu DB
			//---------------------------------------------------------
			var export = new GradeExport
			{
				ExamId = id,
				UserId = userId,
				Url = url,
				CreatedAt = DateTime.UtcNow
			};

			try
			{
				await _unitOfWork.GradeExportRepository.AddAsync(export);
				await _unitOfWork.SaveChangesAsync();
			}
			catch
			{
				throw new AppException("Failed to save export history to database", 500);
			}

			return new GradeExportResponse { Url = _s3Service.GetPresignedUrlFromFullUrl(url) };
		}

		private Row? FindRowByStudentCode(SpreadsheetDocument doc, WorksheetPart wsPart, List<Row> rows, string studentCode)
		{
			int studentColIndex = 1; // B column

			foreach (var row in rows)
			{
				var cell = GetOrCreateCell(wsPart, row, studentColIndex);
				string value = GetCellValue(doc, cell);

				if (!string.IsNullOrWhiteSpace(value) &&
					value.Trim().Equals(studentCode.Trim(), StringComparison.OrdinalIgnoreCase))
				{
					return row;
				}
			}

			return null;
		}

		private void HideOtherTeacherRows(SpreadsheetDocument doc, WorksheetPart wsPart, string teacherCode)
		{
			var sheetData = wsPart.Worksheet.GetFirstChild<SheetData>();
			var rows = sheetData.Elements<Row>().ToList();

			int markerColIndex = 2; // Column C

			foreach (var row in rows.Where(r => r.RowIndex >= 3))
			{
				var markerCell = GetOrCreateCell(wsPart, row, markerColIndex);
				string markerValue = GetCellValue(doc, markerCell)?.Trim() ?? "";

				if (!markerValue.Equals(teacherCode.Trim(), StringComparison.OrdinalIgnoreCase))
				{
					row.Hidden = true;    // ⭐ Chỉ ẩn, không xóa
				}
			}
		}

		private Cell GetOrCreateCell(WorksheetPart wsPart, Row row, int colIndex)
		{
			string columnName = GetColumnName(colIndex);
			string cellReference = columnName + row.RowIndex;

			Cell? cell = row.Elements<Cell>()
						   .FirstOrDefault(c => c.CellReference?.Value == cellReference);

			if (cell == null)
			{
				cell = new Cell { CellReference = cellReference };

				Cell? refCell = null;
				foreach (Cell c in row.Elements<Cell>())
				{
					if (string.Compare(c.CellReference.Value, cellReference, true) > 0)
					{
						refCell = c;
						break;
					}
				}

				row.InsertBefore(cell, refCell);
			}

			return cell;
		}

		private string GetColumnName(int index)
		{
			int dividend = index + 1;
			string columnName = "";

			while (dividend > 0)
			{
				int modulo = (dividend - 1) % 26;
				columnName = Convert.ToChar(65 + modulo) + columnName;
				dividend = (dividend - modulo) / 26;
			}

			return columnName;
		}

		private int GetMaxColumnIndex(Row row)
		{
			int max = 0;
			foreach (var cell in row.Elements<Cell>())
			{
				var reference = cell.CellReference?.Value;
				if (string.IsNullOrWhiteSpace(reference))
					continue;

				int current = GetColumnIndexFromCellReference(reference);
				if (current > max)
					max = current;
			}

			return max;
		}

		private int GetColumnIndexFromCellReference(string cellReference)
		{
			int index = 0;
			foreach (char ch in cellReference)
			{
				if (!char.IsLetter(ch))
					break;

				index = (index * 26) + (char.ToUpperInvariant(ch) - 'A' + 1);
			}

			return Math.Max(0, index - 1); // zero-based
		}


		private string GetS3KeyFromUrl(string url)
		{
			var uri = new Uri(url);

			// AbsolutePath => trả về decode nhưng dấu + vẫn giữ nguyên
			var path = Uri.UnescapeDataString(uri.AbsolutePath);

			// Trong S3, folder/file name chứa space phải là " " không phải "+"
			path = path.Replace("+", " ");

			return path.TrimStart('/');
		}

		private async Task<MemoryStream> DownloadFromS3Async(string key)
		{
			var request = new GetObjectRequest
			{
				BucketName = _awsConfig.BucketName,
				Key = key
			};

			var response = await _s3Client.GetObjectAsync(request);

			var ms = new MemoryStream();
			await response.ResponseStream.CopyToAsync(ms);
			ms.Position = 0;

			return ms;
		}

		private MemoryStream CloneStream(Stream original)
		{
			var clone = new MemoryStream();
			original.Position = 0;
			original.CopyTo(clone);
			clone.Position = 0;
			original.Position = 0;
			return clone;
		}

		public MemoryStream ConvertXmlExcelToXlsx(Stream xmlFile)
		{
			// Set license for EPPlus 8.x
			ExcelPackage.License.SetNonCommercialPersonal("MyProject");

			using var package = new ExcelPackage(xmlFile);

			var ms = new MemoryStream();
			package.SaveAs(ms);
			ms.Position = 0;

			return ms;
		}

		public async Task<PagingResponse<ExamResponse>> GetAssignedExam(ExamFilter filter, int userId)
		{
			if (filter.Page <= 0)
				throw new AppException("Page number must be greater than or equal to 1", 400);

			if (filter.Size < 0)
				throw new AppException("Size must not be negative", 400);

			var filters = new List<Expression<Func<Exam, bool>>>();

			// Teacher được gán grading exam
			filters.Add(e => e.ExamStudents.Any(es => es.TeacherId == userId));

			var skip = (filter.Page - 1) * filter.Size;

			// Sort newest first
			Func<IQueryable<Exam>, IOrderedQueryable<Exam>> orderBy =
				q => q.OrderByDescending(o => o.CreatedAt);
			var totalItems = await _unitOfWork.ExamRepository.CountAsync(filters);

			var data = await _unitOfWork.ExamRepository.GetPagedAsync<Exam>(
				skip,
				filter.Size,
				filters,
				orderBy,
				include: q => q.Include(x => x.ExamStudents),
				null,
				asNoTracking: true
			);
			var responses = _mapper.Map<IEnumerable<ExamResponse>>(data.ToList());
			foreach (var res in responses)
			{
				res.ExamPaper = _s3Service.GetPresignedUrlFromFullUrl(res.ExamPaper);
			}
			return new PagingResponse<ExamResponse>
			{
				Result = responses,
				Page = filter.Page,
				Size = filter.Size,
				TotalItems = totalItems,
				TotalPages = (int)Math.Ceiling(totalItems / (double)filter.Size)
			};
		}

		public async Task<List<GradeExportResponse>> GetGradeHistory(long id)
		{
			var responses = await _unitOfWork.GradeExportRepository.GetGradeExportByExamId(id);
			var mapped = _mapper.Map<List<GradeExportResponse>>(responses);
			foreach (var res in mapped)
			{
				res.Url = _s3Service.GetPresignedUrlFromFullUrl(res.Url);
			}
			return mapped;
		}

		public async Task<List<GradeExportResponse>> GetMyGradeHistory(int teacherId, long id)
		{
			var responses = await _unitOfWork.GradeExportRepository.GetGradeExportByTeacherIdAndExamId(teacherId, id);
			var mapped = _mapper.Map<List<GradeExportResponse>>(responses);
			foreach (var res in mapped)
			{
				res.Url = _s3Service.GetPresignedUrlFromFullUrl(res.Url);
			}
			return mapped;
		}

		public async Task<int> ParseDocxQuestions(long examId, IFormFile file)
		{
			using var ms = new MemoryStream();
			await file.CopyToAsync(ms);
			ms.Position = 0;
			return await ParseDocxQuestionsFromStream(examId, ms);
		}

		public async Task<int> ParseDocxQuestionsFromPath(long examId, string filePath)
		{
			using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read);
			return await ParseDocxQuestionsFromStream(examId, fs);
		}

		private async Task<int> ParseDocxQuestionsFromStream(long examId, Stream stream)
		{
			using var doc = WordprocessingDocument.Open(stream, false);
			var body = doc.MainDocumentPart?.Document.Body;
			if (body == null) return 0;

			var paragraphs = body.Elements<DocumentFormat.OpenXml.Wordprocessing.Paragraph>().ToList();

			int questionCount = 0;
			ExamQuestion? currentQuestion = null;
			StringBuilder questionText = new();

			// Regex to match "Question 1:", "Câu 1:", "Q1.", etc.
			var questionRegex = new System.Text.RegularExpressions.Regex(@"^(Question|Câu|Q|C)\s*(\d+)\s*[:.]", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

			foreach (var para in paragraphs)
			{
				string text = para.InnerText.Trim();
				if (string.IsNullOrWhiteSpace(text)) continue;

				var match = questionRegex.Match(text);
				if (match.Success)
				{
					// Save previous question
					if (currentQuestion != null)
					{
						currentQuestion.QuestionText = questionText.ToString().Trim();
						await _unitOfWork.ExamQuestionRepository.AddAsync(currentQuestion);
						questionCount++;
					}

					// Start new question
					int qNum = int.Parse(match.Groups[2].Value);
					currentQuestion = new ExamQuestion
					{
						ExamId = examId,
						QuestionNumber = qNum,
						MaxScore = 0 
					};
					questionText = new StringBuilder();
					questionText.AppendLine(text);
				}
				else if (currentQuestion != null)
				{
					questionText.AppendLine(text);
				}
			}

			// Save the last question
			if (currentQuestion != null)
			{
				currentQuestion.QuestionText = questionText.ToString().Trim();
				await _unitOfWork.ExamQuestionRepository.AddAsync(currentQuestion);
				questionCount++;
			}

			await _unitOfWork.SaveChangesAsync();
			return questionCount;
		}

		public async Task<long?> GetNextStudentId(long currentExamStudentId)
		{
			var currentStudent = await _unitOfWork.ExamStudentRepository.GetByIdAsync(currentExamStudentId);
			if (currentStudent == null) return null;

			// Find the next student in the same exam by Id (which follows the import order)
			var nextStudent = await _unitOfWork.ExamStudentRepository
				.Query(asNoTracking: true)
				.Where(es => es.ExamId == currentStudent.ExamId && es.Id > currentExamStudentId)
				.OrderBy(es => es.Id)
				.FirstOrDefaultAsync();

			return nextStudent?.Id;
		}
		public async Task<string?> GetPaperInline(long id)
		{
			var exam = await _unitOfWork.ExamRepository.GetByIdAsync(id);
			if (exam == null) return null;

			if (string.IsNullOrEmpty(exam.ExamPaper)) return null;

			// If it's a full S3 URL or path, get a presigned URL
			if (exam.ExamPaper.Contains("s3.amazonaws.com") || exam.ExamPaper.StartsWith(exam.ExamCode))
			{
				return _s3Service.GetPresignedUrlFromFullUrl(exam.ExamPaper);
			}

			return exam.ExamPaper;
		}

		public async Task<DocFile?> GetDocFileById(long docFileId)
		{
			return await _unitOfWork.DocFileRepository.GetByIdAsync(docFileId);
		}

		public async Task<int> SyncQuestionsFromDocFiles(long examId)
		{
			// Get all student IDs for this exam
			var studentIds = await _unitOfWork.ExamStudentRepository
				.Query(asNoTracking: true)
				.Where(es => es.ExamId == examId)
				.Select(es => es.Id)
				.ToListAsync();

			if (!studentIds.Any()) return 0;

			// Find all unique question numbers from split doc files
			var questionNumbers = await _unitOfWork.DocFileRepository
				.Query(asNoTracking: true)
				.Where(df => studentIds.Contains(df.ExamStudentId) && df.QuestionNumber.HasValue && df.QuestionNumber.Value > 0)
				.Select(df => df.QuestionNumber!.Value)
				.Distinct()
				.OrderBy(q => q)
				.ToListAsync();

			if (!questionNumbers.Any()) return 0;

			// Get existing questions
			var existingQuestions = await _unitOfWork.ExamQuestionRepository
				.Query(asNoTracking: false)
				.Where(eq => eq.ExamId == examId)
				.ToListAsync();

			int added = 0;
			foreach (var qNum in questionNumbers)
			{
				if (!existingQuestions.Any(eq => eq.QuestionNumber == qNum))
				{
					var newQuestion = new ExamQuestion
					{
						ExamId = examId,
						QuestionNumber = qNum,
						QuestionText = $"Question {qNum}",
						MaxScore = 10
					};
					await _unitOfWork.ExamQuestionRepository.AddAsync(newQuestion);
					await _unitOfWork.SaveChangesAsync();

					var rubric = new Rubric
					{
						ExamQuestionId = newQuestion.Id,
						Criterion = $"Tiêu chí 1",
						MaxScore = newQuestion.MaxScore,
						OrderIndex = 1
					};
					await _unitOfWork.RubricRepository.AddAsync(rubric);
					added++;
				}
			}

			await _unitOfWork.SaveChangesAsync();
			return added;
		}
	}
}
