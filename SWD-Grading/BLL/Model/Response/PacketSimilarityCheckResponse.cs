using Model.Enums;
using System.Collections.Generic;

namespace BLL.Model.Response
{
	public class PacketSimilarityCheckResponse
	{
		public long? PacketId { get; set; }
		public long ExamId { get; set; }
		public int? QuestionNumber { get; set; }
		public SimilarityScope Scope { get; set; }
		public decimal Threshold { get; set; }
		public int TotalPacketsConsidered { get; set; }
		public int TotalComparisons { get; set; }
		public int FlaggedPairs { get; set; }
		public int CreatedFlags { get; set; }
		public int UpdatedFlags { get; set; }
		public List<SimilarityFlagResponse> Flags { get; set; } = new();
	}
}
