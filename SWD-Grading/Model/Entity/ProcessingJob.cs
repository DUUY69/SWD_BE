using Model.Enums;
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Model.Entity
{
	[Table("ProcessingJob")]
	public class ProcessingJob
	{
		[Key]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public long Id { get; set; }

		[Required]
		public long SubmissionId { get; set; }

		[ForeignKey(nameof(SubmissionId))]
		public Submission Submission { get; set; } = null!;

		[MaxLength(100)]
		public string? JobType { get; set; }

		[Required]
		public ProcessingJobStatus Status { get; set; } = ProcessingJobStatus.Pending;

		public int ProgressPercent { get; set; }

		public int AttemptCount { get; set; }

		public string? ErrorMessage { get; set; }

		[Required]
		public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

		[Required]
		public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

		public DateTime? StartedAt { get; set; }

		public DateTime? FinishedAt { get; set; }
	}
}
