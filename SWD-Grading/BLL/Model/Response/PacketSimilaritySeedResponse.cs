using System;
using System.Collections.Generic;

namespace BLL.Model.Response
{
	public class PacketSimilaritySeedResponse
	{
		public long ExamId { get; set; }
		public int SeededStudents { get; set; }
		public int SeededQuestions { get; set; }
		public int SeededSubmissions { get; set; }
		public int SeededPackets { get; set; }
		public decimal RecommendedThreshold { get; set; }
		public List<long> SubmissionIds { get; set; } = new();
		public DateTime SeededAt { get; set; }
		public string Message { get; set; } = string.Empty;
	}
}
