using Model.Enums;

namespace BLL.Model.Response
{
	public class PacketSimilarityThresholdResponse
	{
		public long ExamId { get; set; }
		public SimilarityScope Scope { get; set; }
		public int? QuestionNumber { get; set; }
		public decimal RecommendedThreshold { get; set; }
		public decimal BaselineThreshold { get; set; }
		public decimal MeanScore { get; set; }
		public decimal StandardDeviation { get; set; }
		public decimal Percentile90Score { get; set; }
		public decimal MaxObservedScore { get; set; }
		public int PairCount { get; set; }
		public string Reason { get; set; } = string.Empty;
	}
}
