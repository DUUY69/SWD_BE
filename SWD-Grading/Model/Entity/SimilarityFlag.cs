using Model.Enums;
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Model.Entity
{
	[Table("SimilarityFlag")]
	public class SimilarityFlag
	{
		[Key]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public long Id { get; set; }

		[Required]
		public long QuestionPacketId { get; set; }

		[ForeignKey(nameof(QuestionPacketId))]
		public QuestionPacket QuestionPacket { get; set; } = null!;

		[Required]
		public long MatchedQuestionPacketId { get; set; }

		[ForeignKey(nameof(MatchedQuestionPacketId))]
		public QuestionPacket MatchedQuestionPacket { get; set; } = null!;

		[Required]
		[Column(TypeName = "DECIMAL(5,4)")]
		public decimal SimilarityScore { get; set; }

		[Required]
		[Column(TypeName = "DECIMAL(5,4)")]
		public decimal ThresholdUsed { get; set; }

		[Required]
		public SimilarityScope Source { get; set; } = SimilarityScope.SameQuestion;

		[Required]
		public FlagReviewStatus ReviewStatus { get; set; } = FlagReviewStatus.Pending;

		public bool? TeacherDecision { get; set; }

		public int? ReviewedByUserId { get; set; }

		[ForeignKey(nameof(ReviewedByUserId))]
		public User? ReviewedByUser { get; set; }

		public DateTime? ReviewedAt { get; set; }

		[MaxLength(500)]
		public string? TeacherNotes { get; set; }

		[Required]
		public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
	}
}
