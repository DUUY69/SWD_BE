using Model.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Model.Entity
{
	public class User
	{
		public int Id { get; set; }
		public string Username { get; set; } = string.Empty;
		public string? TeacherCode { get; set; }
		public string PasswordHash { get; set; } = string.Empty;
		public bool IsActive { get; set; }
		public UserRole Role { get; set; }
		public List<GradeExport> GradeExports { get; set; } = new();
		public List<Flag> ReviewedFlags { get; set; } = new();
	}
}
