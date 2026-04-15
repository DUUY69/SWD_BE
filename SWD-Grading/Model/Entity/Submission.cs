using Model.Enums;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Model.Entity
{
	[Table("Submission")]
	public class Submission
	{
		[Key]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public long Id { get; set; }

		[Required]
		public long ExamId { get; set; }

		[ForeignKey(nameof(ExamId))]
		public Exam Exam { get; set; } = null!;

		[Required]
		public long ExamStudentId { get; set; }

		[ForeignKey(nameof(ExamStudentId))]
		public ExamStudent ExamStudent { get; set; } = null!;

		[Required]
		public int Attempt { get; set; }

		[MaxLength(255)]
		public string? OriginalFileName { get; set; }

		[MaxLength(500)]
		public string? OriginalFileUrl { get; set; }

		[MaxLength(50)]
		public string? SourceFormat { get; set; }

		[Required]
		public SubmissionStatus Status { get; set; } = SubmissionStatus.Pending;

		public string? FailureReason { get; set; }

		[Required]
		public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

		[Required]
		public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

		public ICollection<QuestionPacket> QuestionPackets { get; set; } = new List<QuestionPacket>();

		public ICollection<ProcessingJob> ProcessingJobs { get; set; } = new List<ProcessingJob>();
	}
}
