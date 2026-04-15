using Model.Enums;
using System;

namespace BLL.Model.Response
{
	public class QuestionPacketResponse
	{
		public long Id { get; set; }
		public long SubmissionId { get; set; }
		public long ExamId { get; set; }
		public long ExamStudentId { get; set; }
		public long ExamQuestionId { get; set; }
		public int QuestionNumber { get; set; }
		public string? ExtractedAnswerText { get; set; }
		public string? PrimaryImageUrl { get; set; }
		public string? ImageUrisJson { get; set; }
		public QuestionPacketStatus Status { get; set; }
		public decimal? ParseConfidence { get; set; }
		public string? ParseNotes { get; set; }
		public DateTime CreatedAt { get; set; }
		public DateTime UpdatedAt { get; set; }
		public string StudentCode { get; set; } = string.Empty;
		public string? StudentName { get; set; }
		public int TeacherId { get; set; }
		public int SubmissionAttempt { get; set; }
		public string? OriginalFileName { get; set; }
		public string? OriginalFileUrl { get; set; }
		public string? SourceFormat { get; set; }
	}
}
