using Model.Enums;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Model.Entity
{
	[Table("question_packet")]
	public class QuestionPacket
	{
		[Key]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public long Id { get; set; }

		[Required]
		public long SubmissionId { get; set; }

		[ForeignKey(nameof(SubmissionId))]
		public Submission Submission { get; set; } = null!;

		[Required]
		public long ExamId { get; set; }

		[ForeignKey(nameof(ExamId))]
		public Exam Exam { get; set; } = null!;

		[Required]
		public long ExamStudentId { get; set; }

		[ForeignKey(nameof(ExamStudentId))]
		public ExamStudent ExamStudent { get; set; } = null!;

		public long? ExamQuestionId { get; set; }

		[ForeignKey(nameof(ExamQuestionId))]
		public ExamQuestion? ExamQuestion { get; set; }

		[Required]
		public int QuestionNumber { get; set; }

		public string? ExtractedAnswerText { get; set; }

		[MaxLength(500)]
		public string? PrimaryImageUrl { get; set; }

		public string? ImageUrlsJson { get; set; }

		[Required]
		public QuestionPacketStatus Status { get; set; } = QuestionPacketStatus.PENDING;

		public string? ParseNotes { get; set; }

		[Column(TypeName = "DECIMAL(5,2)")]
		public decimal? ParseConfidence { get; set; }

		public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

		public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

		public ICollection<Flag> SourceFlags { get; set; } = new List<Flag>();

		public ICollection<Flag> MatchedFlags { get; set; } = new List<Flag>();
	}
}
