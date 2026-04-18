using BLL.Interface;
using BLL.Model.Response;
using DAL.Interface;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
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
		private readonly decimal _sameQuestionBaselineThreshold;
		private readonly decimal _globalBaselineThreshold;
		private readonly decimal _maxRecommendedThreshold;

		public PacketSimilarityService(
			IUnitOfWork unitOfWork,
			IVectorService vectorService,
			IAIVerificationService aiVerificationService,
			IConfiguration configuration,
			ILogger<PacketSimilarityService> logger)
		{
			_unitOfWork = unitOfWork;
			_vectorService = vectorService;
			_aiVerificationService = aiVerificationService;
			_logger = logger;
			_sameQuestionBaselineThreshold = GetConfiguredThreshold(configuration, "PacketSimilarity:SameQuestionBaselineThreshold", 0.78m);
			_globalBaselineThreshold = GetConfiguredThreshold(configuration, "PacketSimilarity:GlobalBaselineThreshold", 0.72m);
			_maxRecommendedThreshold = GetConfiguredThreshold(configuration, "PacketSimilarity:MaxRecommendedThreshold", 0.95m);
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

		public async Task<PacketSimilarityCheckResponse> CheckPacketAsync(long packetId, decimal? threshold, SimilarityScope scope, int userId)
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
			var targetScores = BuildSinglePacketScores(targetPacket, candidates, embeddings, scope);
			var thresholdSummary = BuildThresholdSummary(targetScores, scope);
			var appliedThreshold = threshold ?? thresholdSummary.RecommendedThreshold;
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
				if (score < (float)appliedThreshold)
				{
					continue;
				}

				var (primaryId, matchedId) = NormalizePair(targetPacket.Id, candidate.Id);
				var key = BuildFlagKey(primaryId, matchedId, scope);

				if (existingFlags.TryGetValue(key, out var existingFlag))
				{
					existingFlag.SimilarityScore = (decimal)score;
					existingFlag.ThresholdUsed = appliedThreshold;
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
					ThresholdUsed = appliedThreshold,
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
				Threshold = appliedThreshold,
				SuggestedThreshold = thresholdSummary.RecommendedThreshold,
				ThresholdSource = threshold.HasValue ? "Manual" : "Recommended",
				ThresholdPairCount = thresholdSummary.PairCount,
				TotalPacketsConsidered = comparablePackets.Count,
				TotalComparisons = totalComparisons,
				FlaggedPairs = flags.Count,
				CreatedFlags = createdFlags,
				UpdatedFlags = updatedFlags,
				Flags = flags.Select(MapFlagResponse).ToList()
			};
		}

		public async Task<PacketSimilarityCheckResponse> CheckExamPacketsAsync(long examId, decimal? threshold, SimilarityScope scope, int userId, int? questionNumber)
		{
			var user = await GetUserAsync(userId);
			var packets = await ApplyTeacherVisibility(BuildPacketQuery(examId, questionNumber), userId, user.Role)
				.OrderBy(packet => packet.QuestionNumber)
				.ThenBy(packet => packet.Id)
				.ToListAsync();

			if (packets.Count == 0)
			{
				throw new ArgumentException($"No ready packets found for exam {examId}");
			}

			var embeddings = await GenerateEmbeddingsAsync(packets);
			var allScores = BuildExamScores(packets, embeddings, scope, userId, user.Role);
			var thresholdSummary = BuildThresholdSummary(allScores, scope);
			var appliedThreshold = threshold ?? thresholdSummary.RecommendedThreshold;
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
						if (score < (float)appliedThreshold)
						{
							continue;
						}

						var (primaryId, matchedId) = NormalizePair(left.Id, right.Id);
						var key = BuildFlagKey(primaryId, matchedId, scope);

						if (existingFlags.TryGetValue(key, out var existingFlag))
						{
							existingFlag.SimilarityScore = (decimal)score;
							existingFlag.ThresholdUsed = appliedThreshold;
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
							ThresholdUsed = appliedThreshold,
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
				Threshold = appliedThreshold,
				SuggestedThreshold = thresholdSummary.RecommendedThreshold,
				ThresholdSource = threshold.HasValue ? "Manual" : "Recommended",
				ThresholdPairCount = thresholdSummary.PairCount,
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

		public async Task<PacketSimilarityThresholdResponse> GetThresholdRecommendationAsync(long examId, SimilarityScope scope, int userId, int? questionNumber)
		{
			var user = await GetUserAsync(userId);
			var packets = await ApplyTeacherVisibility(BuildPacketQuery(examId, questionNumber), userId, user.Role)
				.OrderBy(packet => packet.QuestionNumber)
				.ThenBy(packet => packet.Id)
				.ToListAsync();

			if (packets.Count < 2)
			{
				var baseline = GetBaselineThreshold(scope);
				return new PacketSimilarityThresholdResponse
				{
					ExamId = examId,
					Scope = scope,
					QuestionNumber = questionNumber,
					RecommendedThreshold = baseline,
					BaselineThreshold = baseline,
					Reason = "Not enough ready packets to derive a data-driven threshold."
				};
			}

			var embeddings = await GenerateEmbeddingsAsync(packets);
			var scores = BuildExamScores(packets, embeddings, scope, userId, user.Role);
			var thresholdSummary = BuildThresholdSummary(scores, scope);

			return new PacketSimilarityThresholdResponse
			{
				ExamId = examId,
				Scope = scope,
				QuestionNumber = questionNumber,
				RecommendedThreshold = thresholdSummary.RecommendedThreshold,
				BaselineThreshold = thresholdSummary.BaselineThreshold,
				MeanScore = thresholdSummary.MeanScore,
				StandardDeviation = thresholdSummary.StandardDeviation,
				Percentile90Score = thresholdSummary.Percentile90Score,
				MaxObservedScore = thresholdSummary.MaxObservedScore,
				PairCount = thresholdSummary.PairCount,
				Reason = thresholdSummary.Reason
			};
		}

		public async Task<PacketSimilaritySeedResponse> SeedTestDataAsync(long examId, int userId, int studentCount, int questionCount)
		{
			var user = await GetUserAsync(userId);
			var exam = await _unitOfWork.ExamRepository.GetByIdAsync(examId);
			if (exam == null)
			{
				throw new ArgumentException($"Exam with ID {examId} not found");
			}

			var examStudentsQuery = _unitOfWork.GetRepository<ExamStudent, long>()
				.Query(false)
				.Include(item => item.Student)
				.Where(item => item.ExamId == examId);

			if (user.Role == UserRole.TEACHER)
			{
				examStudentsQuery = examStudentsQuery.Where(item => item.TeacherId == userId);
			}

			var examStudents = await examStudentsQuery
				.OrderBy(item => item.Student.StudentCode)
				.Take(studentCount)
				.ToListAsync();

			if (examStudents.Count < 2)
			{
				throw new InvalidOperationException("At least two exam students are required to seed packet similarity data.");
			}

			var examQuestions = await _unitOfWork.GetRepository<ExamQuestion, long>()
				.Query(false)
				.Where(item => item.ExamId == examId)
				.OrderBy(item => item.QuestionNumber)
				.Take(questionCount)
				.ToListAsync();

			if (examQuestions.Count == 0)
			{
				throw new InvalidOperationException("The exam must contain at least one question before seeding packet similarity data.");
			}

			var submissionRepo = _unitOfWork.GetRepository<Submission, long>();
			var packetRepo = _unitOfWork.GetRepository<QuestionPacket, long>();
			var submissionIds = new List<long>();
			var packets = new List<QuestionPacket>();

			await using var transaction = await _unitOfWork.BeginTransactionAsync();

			foreach (var examStudent in examStudents)
			{
				var nextAttempt = await submissionRepo.Query(false)
					.Where(item => item.ExamStudentId == examStudent.Id)
					.Select(item => (int?)item.Attempt)
					.MaxAsync() ?? 0;

				var submission = new Submission
				{
					ExamId = examId,
					ExamStudentId = examStudent.Id,
					Attempt = nextAttempt + 1,
					OriginalFileName = $"seeded-packet-similarity-{DateTime.UtcNow:yyyyMMddHHmmss}-{examStudent.Student.StudentCode}.docx",
					OriginalFileUrl = $"seed://{exam.ExamCode}/{examStudent.Student.StudentCode}/attempt-{nextAttempt + 1}",
					SourceFormat = "SEEDED",
					Status = SubmissionStatus.Processed,
					CreatedAt = DateTime.UtcNow,
					UpdatedAt = DateTime.UtcNow
				};

				await submissionRepo.AddAsync(submission);
				await _unitOfWork.SaveChangesAsync();
				submissionIds.Add(submission.Id);

				for (var questionIndex = 0; questionIndex < examQuestions.Count; questionIndex++)
				{
					var question = examQuestions[questionIndex];
					packets.Add(new QuestionPacket
					{
						SubmissionId = submission.Id,
						ExamId = examId,
						ExamStudentId = examStudent.Id,
						ExamQuestionId = question.Id,
						QuestionNumber = question.QuestionNumber,
						ExtractedAnswerText = BuildSeededAnswer(question, examStudent, questionIndex, packets.Count),
						Status = QuestionPacketStatus.Ready,
						ParseConfidence = 98m,
						ParseNotes = "Seeded packet for similarity calibration.",
						CreatedAt = DateTime.UtcNow,
						UpdatedAt = DateTime.UtcNow
					});
				}
			}

			await packetRepo.AddRangeAsync(packets);
			await _unitOfWork.SaveChangesAsync();
			await transaction.CommitAsync();

			var recommendation = await GetThresholdRecommendationAsync(examId, SimilarityScope.SameQuestion, userId, null);

			return new PacketSimilaritySeedResponse
			{
				ExamId = examId,
				SeededStudents = examStudents.Count,
				SeededQuestions = examQuestions.Count,
				SeededSubmissions = submissionIds.Count,
				SeededPackets = packets.Count,
				RecommendedThreshold = recommendation.RecommendedThreshold,
				SubmissionIds = submissionIds,
				SeededAt = DateTime.UtcNow,
				Message = "Seeded ready packets with a mix of highly similar, moderately similar, and distinct answers."
			};
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

		private static IQueryable<QuestionPacket> ApplyTeacherVisibility(IQueryable<QuestionPacket> query, int userId, UserRole role)
		{
			if (role == UserRole.TEACHER)
			{
				return query.Where(packet => packet.ExamStudent.TeacherId == userId);
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

		private List<float> BuildSinglePacketScores(
			QuestionPacket targetPacket,
			List<QuestionPacket> candidates,
			Dictionary<long, float[]> embeddings,
			SimilarityScope scope)
		{
			var scores = new List<float>();

			foreach (var candidate in candidates)
			{
				if (candidate.ExamStudentId == targetPacket.ExamStudentId)
				{
					continue;
				}

				if (scope == SimilarityScope.SameQuestion && candidate.QuestionNumber != targetPacket.QuestionNumber)
				{
					continue;
				}

				scores.Add(CalculateCosineSimilarity(embeddings[targetPacket.Id], embeddings[candidate.Id]));
			}

			return scores;
		}

		private List<float> BuildExamScores(
			List<QuestionPacket> packets,
			Dictionary<long, float[]> embeddings,
			SimilarityScope scope,
			int userId,
			UserRole role)
		{
			var scores = new List<float>();
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

						if (role == UserRole.TEACHER &&
							left.ExamStudent.TeacherId != userId &&
							right.ExamStudent.TeacherId != userId)
						{
							continue;
						}

						scores.Add(CalculateCosineSimilarity(embeddings[left.Id], embeddings[right.Id]));
					}
				}
			}

			return scores;
		}

		private ThresholdSummary BuildThresholdSummary(List<float> scores, SimilarityScope scope)
		{
			var baseline = GetBaselineThreshold(scope);
			if (scores.Count == 0)
			{
				return new ThresholdSummary
				{
					BaselineThreshold = baseline,
					RecommendedThreshold = baseline,
					Reason = "No comparable packet pairs were available, so the baseline threshold was used."
				};
			}

			var orderedScores = scores.OrderBy(score => score).ToList();
			var mean = orderedScores.Average();
			var variance = orderedScores.Average(score => Math.Pow(score - mean, 2));
			var standardDeviation = Math.Sqrt(variance);
			var percentile90 = GetPercentile(orderedScores, 0.9);
			var maxObserved = orderedScores[^1];
			var candidate = Math.Max(percentile90, mean + (standardDeviation * 0.85));
			var recommended = Clamp((decimal)candidate, baseline, _maxRecommendedThreshold);

			return new ThresholdSummary
			{
				BaselineThreshold = baseline,
				RecommendedThreshold = recommended,
				MeanScore = RoundScore((decimal)mean),
				StandardDeviation = RoundScore((decimal)standardDeviation),
				Percentile90Score = RoundScore((decimal)percentile90),
				MaxObservedScore = RoundScore((decimal)maxObserved),
				PairCount = orderedScores.Count,
				Reason = "Recommended from observed pair-score distribution using the stronger of P90 and mean + 0.85*stddev."
			};
		}

		private decimal GetBaselineThreshold(SimilarityScope scope)
		{
			return scope == SimilarityScope.SameQuestion
				? _sameQuestionBaselineThreshold
				: _globalBaselineThreshold;
		}

		private static float GetPercentile(List<float> orderedScores, double percentile)
		{
			if (orderedScores.Count == 0)
			{
				return 0;
			}

			var position = percentile * (orderedScores.Count - 1);
			var lowerIndex = (int)Math.Floor(position);
			var upperIndex = (int)Math.Ceiling(position);

			if (lowerIndex == upperIndex)
			{
				return orderedScores[lowerIndex];
			}

			var fraction = position - lowerIndex;
			return orderedScores[lowerIndex] + ((orderedScores[upperIndex] - orderedScores[lowerIndex]) * (float)fraction);
		}

		private static decimal Clamp(decimal value, decimal min, decimal max)
		{
			return Math.Min(Math.Max(value, min), max);
		}

		private static decimal RoundScore(decimal value)
		{
			return Math.Round(value, 4, MidpointRounding.AwayFromZero);
		}

		private static decimal GetConfiguredThreshold(IConfiguration configuration, string key, decimal fallback)
		{
			var rawValue = configuration[key];
			return decimal.TryParse(rawValue, out var parsedValue) ? parsedValue : fallback;
		}

		private static string BuildSeededAnswer(ExamQuestion question, ExamStudent examStudent, int questionIndex, int packetIndex)
		{
			var topic = string.IsNullOrWhiteSpace(question.QuestionText)
				? $"question {question.QuestionNumber}"
				: question.QuestionText!;
			var studentCode = examStudent.Student.StudentCode;
			var variant = packetIndex % 6;

			var highSimilarityBase =
				$"For {topic}, the answer is organized around three ideas: identify the actors, describe the main flow, and explain the design decision that reduces coupling. " +
				$"The solution uses a service layer to isolate business rules, keeps validation inside the application boundary, and stores persistence logic in dedicated repositories. " +
				$"This structure makes the system easier to test, easier to maintain, and less likely to duplicate logic across controllers.";

			return variant switch
			{
				0 => highSimilarityBase + $" Student {studentCode} also mentions that sequence handling should remain inside the service to keep the controller thin.",
				1 => highSimilarityBase.Replace("organized around three ideas", "built around three key ideas")
					.Replace("reduces coupling", "lowers coupling")
					+ $" Student {studentCode} adds that the controller should only orchestrate the request and response.",
				2 => $"For {topic}, I would still separate actors, process flow, and the design choice behind the implementation. " +
					$"The best approach is to keep business rules in a service layer, leave data access to repositories, and avoid putting validation everywhere. " +
					$"That keeps the code maintainable and makes unit testing easier for student {studentCode}.",
				3 => $"My answer for {topic} focuses on how the feature behaves from the user's perspective. " +
					$"I explain the normal flow first, then the exception flow, and finally the classes that collaborate. " +
					$"Compared with other solutions, this answer uses similar concepts but different examples for student {studentCode}.",
				4 => $"For {topic}, I emphasize trade-offs instead of only the diagram structure. " +
					$"A modular design is useful because each component can evolve independently, but we still need clear contracts, logging, and failure handling. " +
					$"Student {studentCode} uses a different narrative and different examples in this version.",
				_ => $"Student {studentCode} answers {topic} by focusing on testing strategy, observability, and deployment concerns. " +
					$"The explanation discusses rollback safety, monitoring metrics, and how to confirm expected behavior after release. " +
					$"This wording is intentionally more distinct so the seed set contains lower-similarity comparisons."
			};
		}

		private sealed class ThresholdSummary
		{
			public decimal RecommendedThreshold { get; set; }
			public decimal BaselineThreshold { get; set; }
			public decimal MeanScore { get; set; }
			public decimal StandardDeviation { get; set; }
			public decimal Percentile90Score { get; set; }
			public decimal MaxObservedScore { get; set; }
			public int PairCount { get; set; }
			public string Reason { get; set; } = string.Empty;
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
