using BLL.Interface;
using BLL.Model.Response;
using DAL.Interface;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Model.Entity;
using Model.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BLL.Service
{
	public class PacketSimilarityService : IPacketSimilarityService
	{
		private readonly IUnitOfWork _unitOfWork;
		private readonly IVectorService _vectorService;
		private readonly IAIVerificationService _aiVerificationService;
		private readonly ILogger<PacketSimilarityService> _logger;

		public PacketSimilarityService(
			IUnitOfWork unitOfWork,
			IVectorService vectorService,
			IAIVerificationService aiVerificationService,
			ILogger<PacketSimilarityService> logger)
		{
			_unitOfWork = unitOfWork;
			_vectorService = vectorService;
			_aiVerificationService = aiVerificationService;
			_logger = logger;
		}

		public async Task<List<QuestionPacketResponse>> GetPacketsAsync(long examId, int userId, int? questionNumber)
		{
			var user = await GetUserAsync(userId);
			var query = BuildPacketQuery(examId, questionNumber);

			if (user.Role == UserRole.TEACHER)
			{
				query = query.Where(packet => packet.ExamStudent.TeacherId == userId);
			}

			var packets = await query
				.OrderBy(packet => packet.QuestionNumber)
				.ThenBy(packet => packet.ExamStudent.Student.StudentCode)
				.ToListAsync();

			return packets.Select(MapPacketResponse).ToList();
		}

		public async Task<PacketSimilarityCheckResponse> CheckPacketAsync(long packetId, decimal threshold, SimilarityScope scope, int userId)
		{
			var user = await GetUserAsync(userId);
			var packetRepo = _unitOfWork.GetRepository<QuestionPacket, long>();

			var targetPacket = await packetRepo.Query(false)
				.Include(packet => packet.Submission)
				.Include(packet => packet.ExamStudent)
					.ThenInclude(examStudent => examStudent.Student)
				.Include(packet => packet.ExamQuestion)
				.FirstOrDefaultAsync(packet => packet.Id == packetId);

			if (targetPacket == null)
			{
				throw new ArgumentException($"QuestionPacket with ID {packetId} not found");
			}

			EnsurePacketVisible(targetPacket, userId, user.Role);
			EnsurePacketComparable(targetPacket);

			var candidateQuery = BuildPacketQuery(targetPacket.ExamId, null)
				.Where(packet => packet.Id != packetId)
				.Where(packet => scope == SimilarityScope.Global || packet.QuestionNumber == targetPacket.QuestionNumber);

			var candidates = await candidateQuery.ToListAsync();
			var comparablePackets = new List<QuestionPacket> { targetPacket };
			comparablePackets.AddRange(candidates);

			var embeddings = await GenerateEmbeddingsAsync(comparablePackets);
			var existingFlags = await LoadExistingFlagLookupAsync(targetPacket.ExamId, scope);
			var savedFlagIds = new List<long>();
			var createdFlags = 0;
			var updatedFlags = 0;
			var totalComparisons = 0;

			foreach (var candidate in candidates)
			{
				if (candidate.ExamStudentId == targetPacket.ExamStudentId)
				{
					continue;
				}

				totalComparisons++;
				var score = CalculateCosineSimilarity(embeddings[targetPacket.Id], embeddings[candidate.Id]);
				if (score < (float)threshold)
				{
					continue;
				}

				var (primaryId, matchedId) = NormalizePair(targetPacket.Id, candidate.Id);
				var key = BuildFlagKey(primaryId, matchedId, scope);

				if (existingFlags.TryGetValue(key, out var existingFlag))
				{
					existingFlag.SimilarityScore = (decimal)score;
					existingFlag.ThresholdUsed = threshold;
					await _unitOfWork.GetRepository<SimilarityFlag, long>().UpdateAsync(existingFlag);
					savedFlagIds.Add(existingFlag.Id);
					updatedFlags++;
					continue;
				}

				var flag = new SimilarityFlag
				{
					QuestionPacketId = primaryId,
					MatchedQuestionPacketId = matchedId,
					SimilarityScore = (decimal)score,
					ThresholdUsed = threshold,
					Source = scope,
					ReviewStatus = FlagReviewStatus.Pending,
					CreatedAt = DateTime.UtcNow
				};

				await _unitOfWork.GetRepository<SimilarityFlag, long>().AddAsync(flag);
				await _unitOfWork.SaveChangesAsync();

				existingFlags[key] = flag;
				savedFlagIds.Add(flag.Id);
				createdFlags++;
			}

			if (createdFlags == 0 && updatedFlags > 0)
			{
				await _unitOfWork.SaveChangesAsync();
			}

			var flags = await LoadFlagsByIdsAsync(savedFlagIds.Distinct().ToList());

			return new PacketSimilarityCheckResponse
			{
				PacketId = targetPacket.Id,
				ExamId = targetPacket.ExamId,
				QuestionNumber = scope == SimilarityScope.SameQuestion ? targetPacket.QuestionNumber : null,
				Scope = scope,
				Threshold = threshold,
				TotalPacketsConsidered = comparablePackets.Count,
				TotalComparisons = totalComparisons,
				FlaggedPairs = flags.Count,
				CreatedFlags = createdFlags,
				UpdatedFlags = updatedFlags,
				Flags = flags.Select(MapFlagResponse).ToList()
			};
		}

		public async Task<PacketSimilarityCheckResponse> CheckExamPacketsAsync(long examId, decimal threshold, SimilarityScope scope, int userId, int? questionNumber)
		{
			var user = await GetUserAsync(userId);
			var packets = await BuildPacketQuery(examId, questionNumber)
				.OrderBy(packet => packet.QuestionNumber)
				.ThenBy(packet => packet.Id)
				.ToListAsync();

			if (packets.Count == 0)
			{
				throw new ArgumentException($"No ready packets found for exam {examId}");
			}

			var embeddings = await GenerateEmbeddingsAsync(packets);
			var existingFlags = await LoadExistingFlagLookupAsync(examId, scope);
			var savedFlagIds = new List<long>();
			var totalComparisons = 0;
			var createdFlags = 0;
			var updatedFlags = 0;

			var groupedPackets = scope == SimilarityScope.SameQuestion
				? packets.GroupBy(packet => packet.QuestionNumber)
				: new[] { packets.GroupBy(_ => 0).Single() };

			foreach (var group in groupedPackets)
			{
				var groupList = group.ToList();
				for (var i = 0; i < groupList.Count; i++)
				{
					for (var j = i + 1; j < groupList.Count; j++)
					{
						var left = groupList[i];
						var right = groupList[j];

						if (left.ExamStudentId == right.ExamStudentId)
						{
							continue;
						}

						if (user.Role == UserRole.TEACHER &&
							left.ExamStudent.TeacherId != userId &&
							right.ExamStudent.TeacherId != userId)
						{
							continue;
						}

						totalComparisons++;
						var score = CalculateCosineSimilarity(embeddings[left.Id], embeddings[right.Id]);
						if (score < (float)threshold)
						{
							continue;
						}

						var (primaryId, matchedId) = NormalizePair(left.Id, right.Id);
						var key = BuildFlagKey(primaryId, matchedId, scope);

						if (existingFlags.TryGetValue(key, out var existingFlag))
						{
							existingFlag.SimilarityScore = (decimal)score;
							existingFlag.ThresholdUsed = threshold;
							await _unitOfWork.GetRepository<SimilarityFlag, long>().UpdateAsync(existingFlag);
							savedFlagIds.Add(existingFlag.Id);
							updatedFlags++;
							continue;
						}

						var flag = new SimilarityFlag
						{
							QuestionPacketId = primaryId,
							MatchedQuestionPacketId = matchedId,
							SimilarityScore = (decimal)score,
							ThresholdUsed = threshold,
							Source = scope,
							ReviewStatus = FlagReviewStatus.Pending,
							CreatedAt = DateTime.UtcNow
						};

						await _unitOfWork.GetRepository<SimilarityFlag, long>().AddAsync(flag);
						await _unitOfWork.SaveChangesAsync();

						existingFlags[key] = flag;
						savedFlagIds.Add(flag.Id);
						createdFlags++;
					}
				}
			}

			if (createdFlags == 0 && updatedFlags > 0)
			{
				await _unitOfWork.SaveChangesAsync();
			}

			var flags = await LoadFlagsByIdsAsync(savedFlagIds.Distinct().ToList());

			return new PacketSimilarityCheckResponse
			{
				ExamId = examId,
				QuestionNumber = questionNumber,
				Scope = scope,
				Threshold = threshold,
				TotalPacketsConsidered = packets.Count,
				TotalComparisons = totalComparisons,
				FlaggedPairs = flags.Count,
				CreatedFlags = createdFlags,
				UpdatedFlags = updatedFlags,
				Flags = flags.Select(MapFlagResponse).ToList()
			};
		}

		public async Task<List<SimilarityFlagResponse>> GetFlagsAsync(long examId, int userId, FlagReviewStatus? reviewStatus, SimilarityScope? source, int? questionNumber)
		{
			var user = await GetUserAsync(userId);
			var query = BuildFlagQuery(examId, userId, user.Role);

			if (reviewStatus.HasValue)
			{
				query = query.Where(flag => flag.ReviewStatus == reviewStatus.Value);
			}

			if (source.HasValue)
			{
				query = query.Where(flag => flag.Source == source.Value);
			}

			if (questionNumber.HasValue)
			{
				query = query.Where(flag =>
					flag.QuestionPacket.QuestionNumber == questionNumber.Value ||
					flag.MatchedQuestionPacket.QuestionNumber == questionNumber.Value);
			}

			var flags = await query
				.OrderByDescending(flag => flag.SimilarityScore)
				.ThenBy(flag => flag.ReviewStatus)
				.ToListAsync();

			return flags.Select(MapFlagResponse).ToList();
		}

		public async Task<SimilarityFlagResponse> GetFlagByIdAsync(long flagId, int userId)
		{
			var user = await GetUserAsync(userId);
			var flag = await BuildFlagQuery(null, userId, user.Role)
				.FirstOrDefaultAsync(item => item.Id == flagId);

			if (flag == null)
			{
				throw new ArgumentException($"SimilarityFlag with ID {flagId} not found");
			}

			return MapFlagResponse(flag);
		}

		public async Task<SimilarityFlagResponse> VerifyFlagWithAIAsync(long flagId, int userId)
		{
			var user = await GetUserAsync(userId);
			var flag = await BuildFlagQuery(null, userId, user.Role)
				.FirstOrDefaultAsync(item => item.Id == flagId);

			if (flag == null)
			{
				throw new ArgumentException($"SimilarityFlag with ID {flagId} not found");
			}

			if (string.IsNullOrWhiteSpace(flag.QuestionPacket.ExtractedAnswerText) ||
				string.IsNullOrWhiteSpace(flag.MatchedQuestionPacket.ExtractedAnswerText))
			{
				throw new InvalidOperationException("Both packets must contain extracted text before AI verification");
			}

			var aiResult = await _aiVerificationService.VerifyTextSimilarityAsync(
				flag.QuestionPacket.ExtractedAnswerText,
				flag.MatchedQuestionPacket.ExtractedAnswerText,
				flag.QuestionPacket.ExamStudent.Student.StudentCode,
				flag.MatchedQuestionPacket.ExamStudent.Student.StudentCode);

			flag.ReviewStatus = FlagReviewStatus.AIReviewed;
			await _unitOfWork.GetRepository<SimilarityFlag, long>().UpdateAsync(flag);
			await _unitOfWork.SaveChangesAsync();

			var response = MapFlagResponse(flag);
			response.AIVerifiedSimilar = aiResult.IsSimilar;
			response.AIConfidenceScore = aiResult.ConfidenceScore;
			response.AISummary = aiResult.Summary;
			response.AIAnalysis = aiResult.Analysis;
			response.AIVerifiedAt = DateTime.UtcNow;
			return response;
		}

		public async Task<SimilarityFlagResponse> TeacherReviewAsync(long flagId, bool isSimilar, string? notes, int userId)
		{
			var user = await GetUserAsync(userId);
			var flag = await BuildFlagQuery(null, userId, user.Role)
				.FirstOrDefaultAsync(item => item.Id == flagId);

			if (flag == null)
			{
				throw new ArgumentException($"SimilarityFlag with ID {flagId} not found");
			}

			flag.ReviewStatus = FlagReviewStatus.TeacherReviewed;
			flag.TeacherDecision = isSimilar;
			flag.TeacherNotes = notes;
			flag.ReviewedByUserId = user.Id;
			flag.ReviewedAt = DateTime.UtcNow;

			await _unitOfWork.GetRepository<SimilarityFlag, long>().UpdateAsync(flag);
			await _unitOfWork.SaveChangesAsync();

			return MapFlagResponse(flag);
		}

		private async Task<User> GetUserAsync(int userId)
		{
			var user = await _unitOfWork.UserRepository.GetByIdAsync(userId);
			if (user == null)
			{
				throw new ArgumentException($"User with ID {userId} not found");
			}

			return user;
		}

		private IQueryable<QuestionPacket> BuildPacketQuery(long examId, int? questionNumber)
		{
			var packetRepo = _unitOfWork.GetRepository<QuestionPacket, long>();
			var query = packetRepo.Query(false)
				.Include(packet => packet.Submission)
				.Include(packet => packet.ExamStudent)
					.ThenInclude(examStudent => examStudent.Student)
				.Include(packet => packet.ExamQuestion)
				.Where(packet => packet.ExamId == examId)
				.Where(packet => packet.Status == QuestionPacketStatus.Ready)
				.Where(packet => !string.IsNullOrWhiteSpace(packet.ExtractedAnswerText));

			if (questionNumber.HasValue)
			{
				query = query.Where(packet => packet.QuestionNumber == questionNumber.Value);
			}

			return query;
		}

		private IQueryable<SimilarityFlag> BuildFlagQuery(long? examId, int userId, UserRole role)
		{
			var flagRepo = _unitOfWork.GetRepository<SimilarityFlag, long>();
			IQueryable<SimilarityFlag> query = flagRepo.Query(false)
				.Include(flag => flag.ReviewedByUser)
				.Include(flag => flag.QuestionPacket)
					.ThenInclude(packet => packet.Submission)
				.Include(flag => flag.QuestionPacket)
					.ThenInclude(packet => packet.ExamStudent)
						.ThenInclude(examStudent => examStudent.Student)
				.Include(flag => flag.MatchedQuestionPacket)
					.ThenInclude(packet => packet.Submission)
				.Include(flag => flag.MatchedQuestionPacket)
					.ThenInclude(packet => packet.ExamStudent)
						.ThenInclude(examStudent => examStudent.Student);

			if (examId.HasValue)
			{
				query = query.Where(flag => flag.QuestionPacket.ExamId == examId.Value);
			}

			if (role == UserRole.TEACHER)
			{
				query = query.Where(flag =>
					flag.QuestionPacket.ExamStudent.TeacherId == userId ||
					flag.MatchedQuestionPacket.ExamStudent.TeacherId == userId);
			}

			return query;
		}

		private async Task<Dictionary<long, float[]>> GenerateEmbeddingsAsync(IEnumerable<QuestionPacket> packets)
		{
			var packetList = packets
				.Where(packet => !string.IsNullOrWhiteSpace(packet.ExtractedAnswerText) && packet.Status == QuestionPacketStatus.Ready)
				.GroupBy(packet => packet.Id)
				.Select(group => group.First())
				.ToList();

			var embeddingTasks = packetList.ToDictionary(
				packet => packet.Id,
				packet => _vectorService.GenerateEmbeddingAsync(packet.ExtractedAnswerText!));

			await Task.WhenAll(embeddingTasks.Values);

			return embeddingTasks.ToDictionary(task => task.Key, task => task.Value.Result);
		}

		private async Task<Dictionary<string, SimilarityFlag>> LoadExistingFlagLookupAsync(long examId, SimilarityScope scope)
		{
			var flags = await _unitOfWork.GetRepository<SimilarityFlag, long>().Query(false)
				.Include(flag => flag.QuestionPacket)
				.Where(flag => flag.QuestionPacket.ExamId == examId && flag.Source == scope)
				.ToListAsync();

			return flags.ToDictionary(
				flag => BuildFlagKey(flag.QuestionPacketId, flag.MatchedQuestionPacketId, flag.Source),
				flag => flag);
		}

		private async Task<List<SimilarityFlag>> LoadFlagsByIdsAsync(List<long> flagIds)
		{
			if (flagIds.Count == 0)
			{
				return new List<SimilarityFlag>();
			}

			return await _unitOfWork.GetRepository<SimilarityFlag, long>().Query(false)
				.Include(flag => flag.ReviewedByUser)
				.Include(flag => flag.QuestionPacket)
					.ThenInclude(packet => packet.Submission)
				.Include(flag => flag.QuestionPacket)
					.ThenInclude(packet => packet.ExamStudent)
						.ThenInclude(examStudent => examStudent.Student)
				.Include(flag => flag.MatchedQuestionPacket)
					.ThenInclude(packet => packet.Submission)
				.Include(flag => flag.MatchedQuestionPacket)
					.ThenInclude(packet => packet.ExamStudent)
						.ThenInclude(examStudent => examStudent.Student)
				.Where(flag => flagIds.Contains(flag.Id))
				.OrderByDescending(flag => flag.SimilarityScore)
				.ToListAsync();
		}

		private static void EnsurePacketComparable(QuestionPacket packet)
		{
			if (packet.Status != QuestionPacketStatus.Ready || string.IsNullOrWhiteSpace(packet.ExtractedAnswerText))
			{
				throw new InvalidOperationException($"QuestionPacket {packet.Id} is not ready for similarity checking");
			}
		}

		private static void EnsurePacketVisible(QuestionPacket packet, int userId, UserRole role)
		{
			if (role == UserRole.TEACHER && packet.ExamStudent.TeacherId != userId)
			{
				throw new UnauthorizedAccessException("You do not have permission to access this packet");
			}
		}

		private static (long PrimaryId, long MatchedId) NormalizePair(long leftId, long rightId)
		{
			return leftId < rightId ? (leftId, rightId) : (rightId, leftId);
		}

		private static string BuildFlagKey(long leftId, long rightId, SimilarityScope scope)
		{
			return $"{leftId}:{rightId}:{scope}";
		}

		private static float CalculateCosineSimilarity(float[] left, float[] right)
		{
			if (left.Length != right.Length)
			{
				throw new ArgumentException("Vectors must have the same length");
			}

			float dotProduct = 0;
			float leftMagnitude = 0;
			float rightMagnitude = 0;

			for (var index = 0; index < left.Length; index++)
			{
				dotProduct += left[index] * right[index];
				leftMagnitude += left[index] * left[index];
				rightMagnitude += right[index] * right[index];
			}

			if (leftMagnitude == 0 || rightMagnitude == 0)
			{
				return 0;
			}

			return dotProduct / ((float)Math.Sqrt(leftMagnitude) * (float)Math.Sqrt(rightMagnitude));
		}

		private SimilarityFlagResponse MapFlagResponse(SimilarityFlag flag)
		{
			return new SimilarityFlagResponse
			{
				Id = flag.Id,
				SimilarityScore = flag.SimilarityScore,
				ThresholdUsed = flag.ThresholdUsed,
				Source = flag.Source,
				ReviewStatus = flag.ReviewStatus,
				ReviewStatusText = flag.ReviewStatus.ToString(),
				ReviewerUsername = flag.ReviewedByUser?.Username,
				TeacherDecision = flag.TeacherDecision,
				TeacherNotes = flag.TeacherNotes,
				ReviewedAt = flag.ReviewedAt,
				CreatedAt = flag.CreatedAt,
				Packet = MapPacketResponse(flag.QuestionPacket),
				MatchedPacket = MapPacketResponse(flag.MatchedQuestionPacket)
			};
		}

		private static QuestionPacketResponse MapPacketResponse(QuestionPacket packet)
		{
			return new QuestionPacketResponse
			{
				Id = packet.Id,
				SubmissionId = packet.SubmissionId,
				ExamId = packet.ExamId,
				ExamStudentId = packet.ExamStudentId,
				ExamQuestionId = packet.ExamQuestionId,
				QuestionNumber = packet.QuestionNumber,
				ExtractedAnswerText = packet.ExtractedAnswerText,
				PrimaryImageUrl = packet.PrimaryImageUrl,
				ImageUrisJson = packet.ImageUrisJson,
				Status = packet.Status,
				ParseConfidence = packet.ParseConfidence,
				ParseNotes = packet.ParseNotes,
				CreatedAt = packet.CreatedAt,
				UpdatedAt = packet.UpdatedAt,
				StudentCode = packet.ExamStudent.Student.StudentCode,
				StudentName = packet.ExamStudent.Student.FullName,
				TeacherId = packet.ExamStudent.TeacherId,
				SubmissionAttempt = packet.Submission.Attempt,
				OriginalFileName = packet.Submission.OriginalFileName,
				OriginalFileUrl = packet.Submission.OriginalFileUrl,
				SourceFormat = packet.Submission.SourceFormat
			};
		}
	}
}
