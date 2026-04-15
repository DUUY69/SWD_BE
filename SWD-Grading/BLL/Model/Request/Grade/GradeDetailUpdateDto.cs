using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BLL.Model.Request.Grade
{
    public class GradeDetailUpdateDto
    {
        public long RubricId { get; set; }
        public decimal Score { get; set; }
    }
}
