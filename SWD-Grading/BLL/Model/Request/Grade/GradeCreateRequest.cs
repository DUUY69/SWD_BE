using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BLL.Model.Request.Grade
{
    public class GradeCreateRequest
    {
        public long ExamStudentId { get; set; }
        public long ExamId { get; set; }
        public decimal TotalScore { get; set; }
        public string? Comment { get; set; }
        public List<GradeDetailUpdateDto> Details { get; set; } = new();
    }
}
