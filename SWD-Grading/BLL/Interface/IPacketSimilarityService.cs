using BLL.Model.Response;
using Model.Enums;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace BLL.Interface
{
	public interface IPacketSimilarityService
	{
		Task<List<QuestionPacketResponse>> GetPacketsAsync(long examId, int userId, int? questionNumber);
		Task<PacketSimilarityCheckResponse> CheckPacketAsync(long packetId, decimal threshold, SimilarityScope scope, int userId);
		Task<PacketSimilarityCheckResponse> CheckExamPacketsAsync(long examId, decimal threshold, SimilarityScope scope, int userId, int? questionNumber);
		Task<List<SimilarityFlagResponse>> GetFlagsAsync(long examId, int userId, FlagReviewStatus? reviewStatus, SimilarityScope? source, int? questionNumber);
		Task<SimilarityFlagResponse> GetFlagByIdAsync(long flagId, int userId);
		Task<SimilarityFlagResponse> VerifyFlagWithAIAsync(long flagId, int userId);
		Task<SimilarityFlagResponse> TeacherReviewAsync(long flagId, bool isSimilar, string? notes, int userId);
	}
}
