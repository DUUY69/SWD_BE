using BLL.Interface;
using BLL.Model.Request;
using BLL.Model.Response;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Model.Enums;
using SWD_Grading.Helper;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SWD_Grading.Controllers
{
	[ApiController]
	[Route("api/question-packets")]
	[Authorize(Roles = "TEACHER,EXAMINATION")]
	public class QuestionPacketController : ControllerBase
	{
		private readonly IPacketSimilarityService _packetSimilarityService;

		public QuestionPacketController(IPacketSimilarityService packetSimilarityService)
		{
			_packetSimilarityService = packetSimilarityService;
		}

		[HttpGet("exam/{examId:long}")]
		public async Task<ActionResult<BaseResponse<List<QuestionPacketResponse>>>> GetPackets(long examId, [FromQuery] int? questionNumber)
		{
			try
			{
				var userId = User.GetUserId();
				var packets = await _packetSimilarityService.GetPacketsAsync(examId, userId, questionNumber);

				return Ok(new BaseResponse<List<QuestionPacketResponse>>
				{
					Success = true,
					Message = "Question packets retrieved successfully.",
					Data = packets
				});
			}
			catch (Exception ex)
			{
				return BuildErrorResponse(ex);
			}
		}

		[HttpPost("{packetId:long}/similarity-check")]
		public async Task<ActionResult<BaseResponse<PacketSimilarityCheckResponse>>> CheckPacket(long packetId, [FromBody] PacketSimilarityCheckRequest request)
		{
			try
			{
				var userId = User.GetUserId();
				var result = await _packetSimilarityService.CheckPacketAsync(packetId, request.Threshold, request.Scope, userId);

				return Ok(new BaseResponse<PacketSimilarityCheckResponse>
				{
					Success = true,
					Message = "Packet similarity check completed successfully.",
					Data = result
				});
			}
			catch (Exception ex)
			{
				return BuildErrorResponse(ex);
			}
		}

		[HttpPost("exam/{examId:long}/similarity-check")]
		public async Task<ActionResult<BaseResponse<PacketSimilarityCheckResponse>>> CheckExamPackets(long examId, [FromBody] PacketSimilarityCheckRequest request)
		{
			try
			{
				var userId = User.GetUserId();
				var result = await _packetSimilarityService.CheckExamPacketsAsync(examId, request.Threshold, request.Scope, userId, request.QuestionNumber);

				return Ok(new BaseResponse<PacketSimilarityCheckResponse>
				{
					Success = true,
					Message = "Exam packet similarity check completed successfully.",
					Data = result
				});
			}
			catch (Exception ex)
			{
				return BuildErrorResponse(ex);
			}
		}

		[HttpGet("exam/{examId:long}/flags")]
		public async Task<ActionResult<BaseResponse<List<SimilarityFlagResponse>>>> GetFlags(
			long examId,
			[FromQuery] FlagReviewStatus? reviewStatus,
			[FromQuery] SimilarityScope? source,
			[FromQuery] int? questionNumber)
		{
			try
			{
				var userId = User.GetUserId();
				var flags = await _packetSimilarityService.GetFlagsAsync(examId, userId, reviewStatus, source, questionNumber);

				return Ok(new BaseResponse<List<SimilarityFlagResponse>>
				{
					Success = true,
					Message = "Similarity flags retrieved successfully.",
					Data = flags
				});
			}
			catch (Exception ex)
			{
				return BuildErrorResponse(ex);
			}
		}

		[HttpGet("flags/{flagId:long}")]
		public async Task<ActionResult<BaseResponse<SimilarityFlagResponse>>> GetFlagById(long flagId)
		{
			try
			{
				var userId = User.GetUserId();
				var flag = await _packetSimilarityService.GetFlagByIdAsync(flagId, userId);

				return Ok(new BaseResponse<SimilarityFlagResponse>
				{
					Success = true,
					Message = "Similarity flag retrieved successfully.",
					Data = flag
				});
			}
			catch (Exception ex)
			{
				return BuildErrorResponse(ex);
			}
		}

		[HttpPost("flags/{flagId:long}/verify-with-ai")]
		public async Task<ActionResult<BaseResponse<SimilarityFlagResponse>>> VerifyFlagWithAI(long flagId)
		{
			try
			{
				var userId = User.GetUserId();
				var flag = await _packetSimilarityService.VerifyFlagWithAIAsync(flagId, userId);

				return Ok(new BaseResponse<SimilarityFlagResponse>
				{
					Success = true,
					Message = "AI verification completed successfully.",
					Data = flag
				});
			}
			catch (Exception ex)
			{
				return BuildErrorResponse(ex);
			}
		}

		[HttpPost("flags/{flagId:long}/teacher-review")]
		public async Task<ActionResult<BaseResponse<SimilarityFlagResponse>>> TeacherReview(long flagId, [FromBody] TeacherReviewSimilarityFlagRequest request)
		{
			try
			{
				var userId = User.GetUserId();
				var flag = await _packetSimilarityService.TeacherReviewAsync(flagId, request.IsSimilar, request.Notes, userId);

				return Ok(new BaseResponse<SimilarityFlagResponse>
				{
					Success = true,
					Message = "Teacher review completed successfully.",
					Data = flag
				});
			}
			catch (Exception ex)
			{
				return BuildErrorResponse(ex);
			}
		}

		private ActionResult BuildErrorResponse(Exception ex)
		{
			return ex switch
			{
				ArgumentException => BadRequest(new BaseResponse<object>
				{
					Success = false,
					Message = ex.Message
				}),
				InvalidOperationException => BadRequest(new BaseResponse<object>
				{
					Success = false,
					Message = ex.Message
				}),
				UnauthorizedAccessException => StatusCode(403, new BaseResponse<object>
				{
					Success = false,
					Message = ex.Message
				}),
				_ => StatusCode(500, new BaseResponse<object>
				{
					Success = false,
					Message = $"Internal server error: {ex.Message}"
				})
			};
		}
	}
}
