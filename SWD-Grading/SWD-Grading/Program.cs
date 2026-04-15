
using Amazon.S3;
using BLL.Interface;
using BLL.Mapper;
using BLL.Service;
using DAL;
using DAL.Interface;
using DAL.Repository;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql.EntityFrameworkCore.PostgreSQL;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using OfficeOpenXml;
using SWD_Grading.Exceptions;
using System.Text;

namespace SWD_Grading
{
	public class Program
	{
		public static void Main(string[] args)
		{
			var builder = WebApplication.CreateBuilder(args);

			// Google service account credential path (supports relative path from content root)
			var credentialPath = builder.Configuration["GoogleCloud:CredentialPath"];
			if (!string.IsNullOrWhiteSpace(credentialPath))
			{
				var absoluteCredentialPath = Path.IsPathRooted(credentialPath)
					? credentialPath
					: Path.Combine(builder.Environment.ContentRootPath, credentialPath);

				if (File.Exists(absoluteCredentialPath))
				{
					Environment.SetEnvironmentVariable("GOOGLE_APPLICATION_CREDENTIALS", absoluteCredentialPath);
				}
			}

			// Add services to the container.

			builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(options =>
			{
				options.MultipartBodyLengthLimit = 524288000;
			});

			builder.WebHost.ConfigureKestrel(options =>
			{
				options.Limits.MaxRequestBodySize = 524288000;
			});

			builder.Services.AddControllers();

			// CORS Configuration
			var allowedOrigins = builder.Configuration.GetSection("CORS:AllowedOrigins").Get<string[]>()
				?? new[]
				{
					"https://swd-fe-vert.vercel.app",
					"https://swd-fe-git-main-duys-projects-fa81d97e.vercel.app",
					"https://swd-edym8ja1o-duys-projects-fa81d97e.vercel.app",
					"https://swd-a82ibidfl-duys-projects-fa81d97e.vercel.app"
				};

			builder.Services.AddCors(options =>
			{
				options.AddPolicy("FrontendPolicy", policy =>
				{
					policy.WithOrigins(allowedOrigins)
						  .AllowAnyMethod()
						  .AllowAnyHeader();
				});
			});

			// JWT Configuration
			var jwtSettings = builder.Configuration.GetSection("JwtSettings");
			var secretKey = jwtSettings["SecretKey"];

			// Database Context (PostgreSQL)
			builder.Services.AddDbContext<SWDGradingDbContext>(options =>
				options
					.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection"))
					.ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning)));

			// Authentication Configuration
			builder.Services.AddAuthentication(options =>
			{
				options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
				options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
			})
			.AddJwtBearer(options =>
			{
				options.TokenValidationParameters = new TokenValidationParameters
				{
					ValidateIssuer = true,
					ValidateAudience = true,
					ValidateLifetime = true,
					ValidateIssuerSigningKey = true,
					ValidIssuer = jwtSettings["Issuer"],
					ValidAudience = jwtSettings["Audience"],
					IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey))
				};
			});

			// Swagger Configuration with JWT
			builder.Services.AddEndpointsApiExplorer();
			builder.Services.AddSwaggerGen(c =>
			{
				c.SwaggerDoc("v1", new OpenApiInfo
				{
					Title = "SWD Grading API",
					Version = "v1",
					Description = "API for SWD Grading System"
				});

				// Add JWT Authentication to Swagger
				c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
				{
					Description = "Nhập token vào đây, hệ thống sẽ tự thêm 'Bearer ' phía trước.",
					Name = "Authorization",
					In = ParameterLocation.Header,
					Type = SecuritySchemeType.Http,
					Scheme = "Bearer",
					BearerFormat = "JWT"
				});

				c.AddSecurityRequirement(new OpenApiSecurityRequirement
				{
					{
						new OpenApiSecurityScheme
						{
							Reference = new OpenApiReference
							{
								Type = ReferenceType.SecurityScheme,
								Id = "Bearer"
							}
						},
						new string[] {}
					}
				});
			});

			// Unit of Work and Generic Repository
			builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();
			builder.Services.AddScoped(typeof(IGenericRepository<,>), typeof(GenericRepository<,>));

			// AWS S3 Configuration
			var awsOptions = builder.Configuration.GetAWSOptions();
			var awsConfig = builder.Configuration.GetSection("AWS");

			awsOptions.Credentials = new Amazon.Runtime.BasicAWSCredentials(
				awsConfig["AccessKey"],
				awsConfig["SecretKey"]
			);
			var regionName = awsConfig["Region"]
				?? Environment.GetEnvironmentVariable("AWS_REGION")
				?? Environment.GetEnvironmentVariable("AWS_DEFAULT_REGION")
				?? "ap-southeast-1";
			awsOptions.Region = Amazon.RegionEndpoint.GetBySystemName(regionName.Trim());

			builder.Services.AddDefaultAWSOptions(awsOptions);
			builder.Services.AddAWSService<IAmazonS3>();

			// Services
			builder.Services.AddScoped<IAuthService, AuthService>();
			builder.Services.AddScoped<IExamService, ExamService>();
			builder.Services.AddScoped<ITesseractOcrService>(sp =>
			{
				var tessdataPath = Path.Combine(AppContext.BaseDirectory, "tessdata");
				var uow = sp.GetRequiredService<IUnitOfWork>();
				var s3 = sp.GetRequiredService<IS3Service>();

				return new TesseractOcrService(tessdataPath, uow, s3);
			});
			builder.Services.AddScoped<IAuthService, AuthService>();
			builder.Services.AddScoped<IExamService, ExamService>();
			builder.Services.AddScoped<IExamStudentService, ExamStudentService>();
			builder.Services.AddScoped<IS3Service, S3Service>();
			builder.Services.AddScoped<IFileProcessingService, FileProcessingService>();
			builder.Services.AddScoped<IExamUploadService, ExamUploadService>();
			builder.Services.AddScoped<IVectorService, VectorService>();
			builder.Services.AddScoped<IAIVerificationService, AIVerificationService>();
			builder.Services.AddScoped<IPlagiarismService, PlagiarismService>();
			builder.Services.AddScoped<IPacketSimilarityService, PacketSimilarityService>();

			// Register BackgroundJobService to automatically process uploaded ZIP files
			builder.Services.AddHostedService<BackgroundJobService>();
			builder.Services.AddScoped<IGradeService, GradeService>();
			builder.Services.AddScoped<IGradeDetailService, GradeDetailService>();

			// Repositories
			builder.Services.AddScoped<IUserRepository, UserRepository>();
			builder.Services.AddScoped<IStudentRepository, StudentRepository>();
			builder.Services.AddScoped<IExamRepository, ExamRepository>();
			builder.Services.AddScoped<IExamZipRepository, ExamZipRepository>();
			builder.Services.AddScoped<IExamStudentRepository, ExamStudentRepository>();
			builder.Services.AddScoped<IDocFileRepository, DocFileRepository>();
			builder.Services.AddScoped<ISimilarityCheckRepository, SimilarityCheckRepository>();
			builder.Services.AddScoped<IRubricRepository, RubricRepository>();
			builder.Services.AddScoped<IExamQuestionRepository, ExamQuestionRepository>();
			builder.Services.AddScoped<IGradeRepository, GradeRepository>();
			builder.Services.AddScoped<IGradeDetailRepository, GradeDetailRepository>();

			// AutoMapper
			builder.Services.AddAutoMapper(typeof(UserProfile).Assembly);
			builder.Services.AddAutoMapper(typeof(ExamProfile).Assembly);
			builder.Services.AddAutoMapper(typeof(ExamQuestionProfile).Assembly);
			builder.Services.AddAutoMapper(typeof(RubricProfile).Assembly);
			builder.Services.AddAutoMapper(typeof(GradeProfile).Assembly);
			builder.Services.AddAutoMapper(typeof(GradeDetailProfile).Assembly);

			var app = builder.Build();

			if (app.Configuration.GetValue<bool>("Database:RunMigrationsOnStart"))
			{
				using var scope = app.Services.CreateScope();
				var db = scope.ServiceProvider.GetRequiredService<SWDGradingDbContext>();
				db.Database.Migrate();
			}

			// Configure the HTTP request pipeline.
			if (app.Environment.IsDevelopment() || true)
			{
				app.UseSwagger();
				app.UseSwaggerUI();
			}

			app.UseCors("FrontendPolicy");

			app.UseHttpsRedirection();
			app.UseAuthentication();
			app.UseAuthorization();

			app.UseMiddleware<GlobalExceptionMiddleware>();
			app.MapControllers();

			app.Run();
		}
	}
}
