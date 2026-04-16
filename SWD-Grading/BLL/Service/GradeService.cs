using Amazon.S3.Model.Internal.MarshallTransformations;
using AutoMapper;
using BLL.Exceptions;
using BLL.Interface;
using BLL.Model.Request;
using BLL.Model.Request.Grade;
using BLL.Model.Response;
using BLL.Model.Response.Grade;
using DAL.Interface;
using Microsoft.EntityFrameworkCore;
using Model.Entity;
using Model.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BLL.Service
{
	public class GradeService : IGradeService
	{
		private readonly IUnitOfWork _unitOfWork;
		private readonly IMapper _mapper;
		public GradeService(IUnitOfWork unitOfWork, IMapper mapper)
		{
			_unitOfWork = unitOfWork;
			_mapper = mapper;
		}

		public async Task<PagingResponse<GradeResponse>> GetAllByExamStudentId(long examStudentId, PagedRequest request)
		{
			var query = _unitOfWork.GradeRepository
				.Query(asNoTracking: true)
				.Where(g => g.ExamStudentId == examStudentId);

			var totalItems = await query.CountAsync();
			var totalPages = (int)Math.Ceiling((double)totalItems / request.PageSize);

			var gradeEntities = await query
				.OrderByDescending(g => g.GradedAt)
				.Skip(request.Skip)
				.Take(request.PageSize)
				.ToListAsync();

			var grades = _mapper.Map<List<GradeResponse>>(gradeEntities);

			var pagedResponse = new PagingResponse<GradeResponse>
			{
				Page = request.PageIndex,
				Size = request.PageSize,
				TotalPages = totalPages,
				TotalItems = totalItems,
				Result = grades
			};

			return pagedResponse;
		}

		public async Task<PagingResponse<GradeResponse>> GetAll(PagedRequest request)
		{
			var query = _unitOfWork.GradeRepository.Query(asNoTracking: true);
			var totalItems = await _unitOfWork.GradeRepository.CountAsync();
			var totalPages = (int)Math.Ceiling((double)totalItems / request.PageSize);
			var gradeEntities = await query
				.OrderByDescending(g => g.GradedAt)
				.Skip(request.Skip)
				.Take(request.PageSize)
				.ToListAsync();

			// Then map in memory (not in SQL)
			var grades = _mapper.Map<List<GradeResponse>>(gradeEntities);

			var pagedResponse = new PagingResponse<GradeResponse>
			{
				Page = request.PageIndex,
				Size = request.PageSize,
				TotalPages = totalPages,
				TotalItems = totalItems,
				Result = grades
			};

			return pagedResponse;
		}

		public async Task<GradeDetailResponse> GetById(long id)
		{
			var grade = await _unitOfWork.GradeRepository.GetById(id);
			var gradeDetailResponse = _mapper.Map<GradeDetailResponse>(grade);
			return gradeDetailResponse;
		}



		public async Task<long> Create(GradeCreateRequest request, string teachercode)
		{
			var newGrade = new Grade
			{
				ExamStudentId = request.ExamStudentId,
				TotalScore = request.TotalScore,
				Comment = request.Comment,
				GradedAt = DateTime.UtcNow,
				GradedBy = teachercode,
				Status = GradeStatus.GRADED
			};
            var existingGrades = await _unitOfWork.GradeRepository.GetByExamStudentId(request.ExamStudentId);
            if (existingGrades.Any())
            {
                int maxAttempt = existingGrades.Max(g => g.Attempt);
                newGrade.Attempt = maxAttempt + 1;
            }
            else
            {
                newGrade.Attempt = 1;
            }
			await _unitOfWork.GradeRepository.AddAsync(newGrade);

			// Update student status to GRADED immediately since it receives valid points
			var existingExamStudent = await _unitOfWork.ExamStudentRepository.GetByIdAsync(request.ExamStudentId);
			if (existingExamStudent != null)
			{
				existingExamStudent.Status = ExamStudentStatus.GRADED;
				await _unitOfWork.ExamStudentRepository.UpdateAsync(existingExamStudent);
			}

			await _unitOfWork.SaveChangesAsync();

            var questions = await _unitOfWork.ExamQuestionRepository
                .GetQuestionByExamId(request.ExamId);

            List<GradeDetail> gradeDetails = new();

            foreach (var question in questions)
            {
                foreach (var rubric in question.Rubrics)
                {
                    // Find if frontend provided a score for this rubric
                    var providedDetail = request.Details.FirstOrDefault(d => d.RubricId == rubric.Id);
                    decimal score = providedDetail != null ? providedDetail.Score : 0;

					gradeDetails.Add(new GradeDetail
					{
						GradeId = newGrade.Id,
						Grade = newGrade,
						RubricId = rubric.Id,
                        Rubric = rubric,
                        Score = score
					});
                }
            }

            await _unitOfWork.GradeDetailRepository.AddRangeAsync(gradeDetails);
			await _unitOfWork.SaveChangesAsync();
            return newGrade.Id;
		}

		public async Task CreateRange(long examId, List<AddGradeRangeRequest> requests)
		{
			var questions = await _unitOfWork.ExamQuestionRepository
				.GetQuestionByExamId(examId);

			List<Grade> grades = new();
			List<GradeDetail> gradeDetails = new();

			foreach (var request in requests)
			{
				var grade = _mapper.Map<Grade>(request);
				grades.Add(grade);

				foreach (var question in questions)
				{
					foreach (var rubric in question.Rubrics)
					{
						gradeDetails.Add(new GradeDetail
						{
							Grade = grade,
							Rubric = rubric
						});
					}
				}
			}

			await _unitOfWork.GradeRepository.AddRangeAsync(grades);
			await _unitOfWork.GradeDetailRepository.AddRangeAsync(gradeDetails);

			await _unitOfWork.SaveChangesAsync();
		}

		public async Task Update(GradeUpdateRequest request, long id)
		{
			// 1. Get the grade using tracking so we don't cause Identity conflicts
			var existingGrade = await _unitOfWork.GradeRepository.GetByIdAsync(id);
			if (existingGrade == null)
			{
				throw new AppException("Grade not found", 404);
			}

			// 2. Update individual rubric scores if provided
			if (request.Details != null && request.Details.Any())
			{
				var existingDetails = await _unitOfWork.GradeDetailRepository.GetByGradeId(id);
				
				foreach (var detailDto in request.Details)
				{
					var detail = existingDetails.FirstOrDefault(d => d.RubricId == detailDto.RubricId);
					if (detail != null)
					{
						detail.Score = detailDto.Score;
						// Entity is tracked, changes will be saved automatically by SaveChangesAsync
					}
				}

				// 3. Recalculate Total Score based on details
				existingGrade.TotalScore = existingDetails.Sum(d => d.Score);
			}
			else
			{
				// Fallback to manual total if no details provided
				existingGrade.TotalScore = request.TotalScore;
			}

			// 4. Update general grade info
			existingGrade.Comment = request.Comment;
			existingGrade.GradedAt = DateTime.UtcNow;
			existingGrade.Status = GradeStatus.GRADED;

			// 5. Update student status to GRADED
			var existingExamStudent = await _unitOfWork.ExamStudentRepository.GetByIdAsync(existingGrade.ExamStudentId);
			if (existingExamStudent != null)
			{
				existingExamStudent.Status = ExamStudentStatus.GRADED;
			}

			await _unitOfWork.SaveChangesAsync();
		}

		public async Task Delete(long id)
		{
			var existingGrade = await _unitOfWork.GradeRepository.GetById(id);
			var existingDetails = await _unitOfWork.GradeDetailRepository.GetByGradeId(id);
			if (existingGrade == null)
			{
				throw new KeyNotFoundException("Grade not found");
			}
			foreach (var detail in existingDetails)
			{
				await _unitOfWork.GradeDetailRepository.RemoveAsync(detail);
			}
			await _unitOfWork.GradeRepository.RemoveAsync(existingGrade);
			await _unitOfWork.SaveChangesAsync();
		}
	}

}
