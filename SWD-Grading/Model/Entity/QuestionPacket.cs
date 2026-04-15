using Model.Enums;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Model.Entity
{
	[Table("QuestionPacket")]
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

		[Required]
		public long ExamQuestionId { get; set; }

		[ForeignKey(nameof(ExamQuestionId))]
		public ExamQuestion ExamQuestion { get; set; } = null!;

		[Required]
		public int QuestionNumber { get; set; }

		public string? ExtractedAnswerText { get; set; }

		[MaxLength(500)]
		public string? PrimaryImageUrl { get; set; }

		public string? ImageUrisJson { get; set; }

		[Required]
		public QuestionPacketStatus Status { get; set; } = QuestionPacketStatus.Pending;

		public string? ParseNotes { get; set; }

		[Column(TypeName = "DECIMAL(5,2)")]
		public decimal? ParseConfidence { get; set; }

		[Required]
		public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

		[Required]
		public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

		public ICollection<SimilarityFlag> PrimaryFlags { get; set; } = new List<SimilarityFlag>();

		public ICollection<SimilarityFlag> MatchedFlags { get; set; } = new List<SimilarityFlag>();
	}
}
