using System.ComponentModel.DataAnnotations;

namespace BLL.Model.Request
{
	public class PacketSimilaritySeedRequest
	{
		[Range(2, 20, ErrorMessage = "StudentCount must be between 2 and 20")]
		public int StudentCount { get; set; } = 6;

		[Range(1, 10, ErrorMessage = "QuestionCount must be between 1 and 10")]
		public int QuestionCount { get; set; } = 3;
	}
}
