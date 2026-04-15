using Model.Enums;
using System.ComponentModel.DataAnnotations;

namespace BLL.Model.Request
{
	public class PacketSimilarityCheckRequest
	{
		[Range(0.0, 1.0, ErrorMessage = "Threshold must be between 0.0 and 1.0")]
		public decimal Threshold { get; set; } = 0.8m;

		public SimilarityScope Scope { get; set; } = SimilarityScope.SameQuestion;

		public int? QuestionNumber { get; set; }
	}
}
