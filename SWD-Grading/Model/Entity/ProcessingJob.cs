using Model.Enums;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Model.Entity
{
	[Table("processing_job")]
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
		public string JobType { get; set; } = "SUBMISSION_PIPELINE";

		[Required]
		public ProcessingJobStatus Status { get; set; } = ProcessingJobStatus.QUEUED;

		public int ProgressPercent { get; set; }

		public int AttemptCount { get; set; }

		public string? ErrorMessage { get; set; }

		public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

		public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

		public DateTime? StartedAt { get; set; }

		public DateTime? FinishedAt { get; set; }
	}
}
