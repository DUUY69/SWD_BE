using Model.Enums;
using System;

namespace BLL.Model.Response
{
	public class SimilarityFlagResponse
	{
		public long Id { get; set; }
		public decimal SimilarityScore { get; set; }
		public decimal ThresholdUsed { get; set; }
		public SimilarityScope Source { get; set; }
		public FlagReviewStatus ReviewStatus { get; set; }
		public string ReviewStatusText { get; set; } = string.Empty;
		public bool? AIVerifiedSimilar { get; set; }
		public decimal? AIConfidenceScore { get; set; }
		public string? AISummary { get; set; }
		public string? AIAnalysis { get; set; }
		public DateTime? AIVerifiedAt { get; set; }
		public string? ReviewerUsername { get; set; }
		public bool? TeacherDecision { get; set; }
		public string? TeacherNotes { get; set; }
		public DateTime? ReviewedAt { get; set; }
		public DateTime CreatedAt { get; set; }
		public QuestionPacketResponse Packet { get; set; } = null!;
		public QuestionPacketResponse MatchedPacket { get; set; } = null!;
	}
}
