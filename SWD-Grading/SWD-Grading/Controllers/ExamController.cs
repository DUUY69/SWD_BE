using BLL.Interface;
using BLL.Model.Request.Exam;
using BLL.Model.Response;
using BLL.Model.Response.Exam;
using BLL.Model.Response.Grade;
using BLL.Service;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Model.Enums;
using Model.Request;
using Model.Response;
using SWD_Grading.Helper;

namespace SWD_Grading.Controllers
{
	[Route("api/exams")]
	[ApiController]
	public class ExamController : ControllerBase
	{

		private readonly IExamService _examService;
		private readonly IExamStudentService _examStudentService;
		private readonly ITesseractOcrService _ocrService;
		private readonly IS3Service _s3Service;
		public ExamController(IExamService examService, ITesseractOcrService ocrService, IExamStudentService examStudentService, IS3Service s3Service)
		{
			_examService = examService;
			_ocrService = ocrService;
			_examStudentService = examStudentService;
			_s3Service = s3Service;
		}

		[HttpPost]
		public async Task<IActionResult> CreateExam([FromBody] CreateExamRequest request)
		{
			BaseResponse<ExamResponse> response = new()
			{
				Code = 201,
				Success = true,
				Message = "Create exam successfully",
				Data = await _examService.CreateExam(request)
			};
			return StatusCode(201, response);
		}
		[HttpGet]
		public async Task<IActionResult> GetAllExams([FromQuery] ExamFilter filter)
		{

			var userRole = User.GetUserRole();
			var result = new PagingResponse<ExamResponse>();
			if (userRole.Equals(UserRole.TEACHER))
			{
				var userId = User.GetUserId();
				result = await _examService.GetAssignedExam(filter, userId);
			}
			else
				result = await _examService.GetAllAsync(filter);
			BaseResponse<PagingResponse<ExamResponse>> response = new()
			{
				Code = 200,
				Success = true,
				Message = "Get all exams successfully",
				Data = result
			};

			return Ok(response);
		}

		[HttpGet("{id}")]
		public async Task<IActionResult> GetExamById(long id)
		{
			var result = await _examService.GetByIdAsync(id);

			BaseResponse<ExamResponse?> response = new()
			{
				Code = 200,
				Success = true,
				Message = "Get exam successfully",
				Data = result
			};

			return Ok(response);
		}

		[HttpPut("{id}")]
		public async Task<IActionResult> UpdateExam(long id, [FromBody] UpdateExamRequest request)
		{
			var result = await _examService.UpdateAsync(id, request);

			BaseResponse<ExamResponse?> response = new()
			{
				Code = 200,
				Success = true,
				Message = "Update exam successfully",
				Data = result
			};

			return Ok(response);
		}

		[HttpDelete("{id}")]
		public async Task<IActionResult> DeleteExam(long id)
		{
			var success = await _examService.DeleteAsync(id);

			BaseResponse<bool> response = new()
			{
				Code = 204,
				Success = true,
				Message = "Delete exam successfully",
				Data = success
			};

			return NoContent();
		}


		[HttpPut("{id}/description")]
		[Consumes("multipart/form-data")]
		public async Task<IActionResult> ExtractText([FromRoute] long id, IFormFile file)
		{
			if (file == null || file.Length == 0)
				return BadRequest("No file uploaded.");

			var tempFilePath = Path.GetTempFileName();

			try
			{
				using (var stream = new FileStream(tempFilePath, FileMode.Create))
				{
					await file.CopyToAsync(stream);
				}

				string url = await _ocrService.ExtractText(id, tempFilePath, file, "eng");

				return Ok(new
				{
					code = 200,
					message = "OCR completed successfully",
					problemStatement = url
				});
			}
			catch (Exception ex)
			{
				// Log lỗi
				Console.WriteLine("OCR ERROR: " + ex);

				return StatusCode(500, new
				{
					code = 500,
					message = "Internal Server Error",
					detail = ex.Message
				});
			}
			finally
			{
				System.IO.File.Delete(tempFilePath);
			}
		}

		[HttpPost("{id}/details")]
		[Consumes("multipart/form-data")]
		public async Task<IActionResult> ParseDetailExcel([FromRoute] long id, IFormFile file)
		{
			if (file == null || file.Length == 0)
				return BadRequest("No file uploaded.");

			await _examService.ParseDetailExcel(id, file);

			return Ok(new
			{
				message = "Import exam details successfully."
			});
		}

		[HttpGet("{examId}/students")]
		public async Task<IActionResult> GetExamStudents(long examId, [FromQuery] ExamStudentFilter filter)
		{
			var userRole = User.GetUserRole();
			var result = new PagingResponse<ExamStudentResponse>();
			if (userRole.Equals(UserRole.TEACHER))
			{
				var userId = User.GetUserId();
				result = await _examStudentService.GetAssignedExamStudent(userId, examId, filter);
			}
			else
				result = await _examStudentService.GetExamStudentsByExamIdAsync(examId, filter);
			BaseResponse<PagingResponse<ExamStudentResponse>> response = new()
			{
				Code = 200,
				Success = true,
				Message = "Get exam students successfully",
				Data = result
			};

			return Ok(response);
		}

		[HttpGet("{id}/questions")]
		public async Task<IActionResult> GetQuestionsByExamId([FromRoute] long id)
		{
			var result = await _examService.GetQuestionByExamId(id);
			BaseResponse<ExamResponse> response = new()
			{
				Code = 200,
				Success = true,
				Message = "Get exam questions successfully",
				Data = result
			};

			return Ok(response);
		}

		[HttpPost("{id}/export-excel")]
		[Authorize]
		public async Task<IActionResult> ExportGradeExcel([FromRoute] long id)
		{
			int userId = User.GetUserId();
			var userRole = User.GetUserRole();
			var result = await _examService.ExportGradeExcel(userId, userRole, id);
			BaseResponse<GradeExportResponse> response = new()
			{
				Code = 200,
				Success = true,
				Message = "Get exam questions successfully",
				Data = result
			};

			return Ok(response);
		}

		[HttpGet("{id}/grade-excel")]
		public async Task<IActionResult> GradeExcelHistory([FromRoute] long id)
		{
			var userRole = User.GetUserRole();
			var result = new List<GradeExportResponse>();
			if (userRole.Equals(UserRole.EXAMINATION))
				result = await _examService.GetGradeHistory(id);
			else
			{
				var userId = User.GetUserId();
				result = await _examService.GetMyGradeHistory(userId, id);
			}
			BaseResponse<List<GradeExportResponse>> response = new()
			{
				Code = 200,
				Success = true,
				Message = "Get exam questions successfully",
				Data = result
			};

			return Ok(response);
		}

		[HttpPost("{id}/questions-docx")]
		[Consumes("multipart/form-data")]
		public async Task<IActionResult> ParseDocxQuestions([FromRoute] long id, IFormFile file)
		{
			if (file == null || file.Length == 0)
				return BadRequest("No file uploaded.");

			int count = await _examService.ParseDocxQuestions(id, file);

			return Ok(new
			{
				code = 200,
				message = $"Successfully extracted {count} questions from DOCX.",
				questionCount = count
			});
		}

		[HttpGet("students/{id}/next")]
		public async Task<IActionResult> GetNextStudent([FromRoute] long id)
		{
			var nextId = await _examService.GetNextStudentId(id);

			return Ok(new
			{
				code = 200,
				nextStudentId = nextId
			});
		}

		/// <summary>
		/// Auto-create missing ExamQuestion records from split doc files
		/// </summary>
		[HttpPost("{id}/sync-questions")]
		public async Task<IActionResult> SyncQuestionsFromDocFiles([FromRoute] long id)
		{
			var count = await _examService.SyncQuestionsFromDocFiles(id);
			return Ok(new
			{
				code = 200,
				success = true,
				message = $"Synced {count} questions from doc files",
				data = count
			});
		}

		/// <summary>
		/// Proxy: stream a doc_file from S3 so Office/Google Docs viewer can render it
		/// </summary>
		[HttpGet("doc-files/{docFileId}/proxy")]
		[AllowAnonymous]
		public async Task<IActionResult> ProxyDocFile([FromRoute] long docFileId)
		{
			try
			{
				var docFile = await _examService.GetDocFileById(docFileId);
				if (docFile == null || string.IsNullOrEmpty(docFile.FilePath))
					return NotFound();

				// Extract S3 key from full URL
				var uri = new Uri(docFile.FilePath);
				string s3Key = uri.AbsolutePath.TrimStart('/');

				var stream = await _s3Service.GetFileAsync(s3Key);
				var contentType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
				return File(stream, contentType, docFile.FileName);
			}
			catch (Exception ex)
			{
				return StatusCode(500, new { message = ex.Message });
			}
		}

		[HttpGet("{id}/paper-inline")]
		public async Task<IActionResult> GetPaperInline([FromRoute] long id)
		{
			var result = await _examService.GetPaperInline(id);
			return Ok(new BaseResponse<string>
			{
				Code = 200,
				Success = true,
				Message = "Get paper inline successfully",
				Data = result
			});
		}
	}
}
