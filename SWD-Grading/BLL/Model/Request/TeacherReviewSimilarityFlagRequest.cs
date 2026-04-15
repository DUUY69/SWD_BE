using System.ComponentModel.DataAnnotations;

namespace BLL.Model.Request
{
	public class TeacherReviewSimilarityFlagRequest
	{
		[Required]
		public bool IsSimilar { get; set; }

		[MaxLength(500)]
		public string? Notes { get; set; }
	}
}
