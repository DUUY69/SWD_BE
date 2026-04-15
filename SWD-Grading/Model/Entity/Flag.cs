using Model.Enums;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Model.Entity
{
	[Table("flag")]
	public class Flag
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

		[Column(TypeName = "DECIMAL(5,4)")]
		public decimal SimilarityScore { get; set; }

		[Column(TypeName = "DECIMAL(5,4)")]
		public decimal ThresholdUsed { get; set; }

		[MaxLength(100)]
		public string Source { get; set; } = "SYSTEM";

		[Required]
		public FlagReviewStatus ReviewStatus { get; set; } = FlagReviewStatus.PENDING;

		public bool? TeacherDecision { get; set; }

		[MaxLength(500)]
		public string? TeacherNotes { get; set; }

		public int? ReviewedByUserId { get; set; }

		[ForeignKey(nameof(ReviewedByUserId))]
		public User? ReviewedByUser { get; set; }

		public DateTime? ReviewedAt { get; set; }

		public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
	}
}
