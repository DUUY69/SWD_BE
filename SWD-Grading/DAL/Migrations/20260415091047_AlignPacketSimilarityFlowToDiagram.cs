using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace DAL.Migrations
{
    /// <inheritdoc />
    public partial class AlignPacketSimilarityFlowToDiagram : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_question_packet_doc_file_DocFileId",
                table: "question_packet");

            migrationBuilder.DropForeignKey(
                name: "FK_question_packet_exam_question_ExamQuestionId",
                table: "question_packet");

            migrationBuilder.DropForeignKey(
                name: "FK_similarity_flag_Users_ReviewedByUserId",
                table: "similarity_flag");

            migrationBuilder.DropForeignKey(
                name: "FK_similarity_flag_question_packet_MatchedQuestionPacketId",
                table: "similarity_flag");

            migrationBuilder.DropForeignKey(
                name: "FK_similarity_flag_question_packet_QuestionPacketId",
                table: "similarity_flag");

            migrationBuilder.DropPrimaryKey(
                name: "PK_similarity_flag",
                table: "similarity_flag");

            migrationBuilder.DropColumn(
                name: "AIVerificationResult",
                table: "similarity_flag");

            migrationBuilder.DropColumn(
                name: "AIVerifiedAt",
                table: "similarity_flag");

            migrationBuilder.DropColumn(
                name: "Notes",
                table: "similarity_flag");

            migrationBuilder.DropColumn(
                name: "TeacherScore",
                table: "similarity_flag");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "similarity_flag");

            migrationBuilder.RenameTable(
                name: "similarity_flag",
                newName: "flag");

            migrationBuilder.RenameColumn(
                name: "PacketStatus",
                table: "question_packet",
                newName: "Status");

            migrationBuilder.RenameColumn(
                name: "ImageUrl",
                table: "question_packet",
                newName: "PrimaryImageUrl");

            migrationBuilder.RenameColumn(
                name: "ExtractedText",
                table: "question_packet",
                newName: "ExtractedAnswerText");

            migrationBuilder.RenameColumn(
                name: "Notes",
                table: "question_packet",
                newName: "ParseNotes");

            migrationBuilder.RenameColumn(
                name: "Confidence",
                table: "question_packet",
                newName: "ParseConfidence");

            migrationBuilder.RenameColumn(
                name: "DocFileId",
                table: "question_packet",
                newName: "SubmissionId");

            migrationBuilder.RenameIndex(
                name: "IX_question_packet_DocFileId",
                table: "question_packet",
                newName: "IX_question_packet_SubmissionId");

            migrationBuilder.RenameColumn(
                name: "Threshold",
                table: "flag",
                newName: "ThresholdUsed");

            migrationBuilder.RenameIndex(
                name: "IX_similarity_flag_ReviewStatus_Source",
                table: "flag",
                newName: "IX_flag_ReviewStatus_Source");

            migrationBuilder.RenameIndex(
                name: "IX_similarity_flag_ReviewedByUserId",
                table: "flag",
                newName: "IX_flag_ReviewedByUserId");

            migrationBuilder.RenameIndex(
                name: "IX_similarity_flag_QuestionPacketId_MatchedQuestionPacketId_So~",
                table: "flag",
                newName: "IX_flag_QuestionPacketId_MatchedQuestionPacketId_Source");

            migrationBuilder.RenameIndex(
                name: "IX_similarity_flag_MatchedQuestionPacketId",
                table: "flag",
                newName: "IX_flag_MatchedQuestionPacketId");

            migrationBuilder.AlterColumn<long>(
                name: "ExamQuestionId",
                table: "question_packet",
                type: "bigint",
                nullable: false,
                defaultValue: 0L,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ImageUrisJson",
                table: "question_packet",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "QuestionNumber",
                table: "doc_file",
                type: "integer",
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ParseNotes",
                table: "question_packet",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(1000)",
                oldMaxLength: 1000,
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "ParseConfidence",
                table: "question_packet",
                type: "numeric(5,2)",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(5,4)",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Source",
                table: "flag",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20);

            migrationBuilder.AlterColumn<string>(
                name: "ReviewStatus",
                table: "flag",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AddColumn<bool>(
                name: "TeacherDecision",
                table: "flag",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TeacherNotes",
                table: "flag",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddPrimaryKey(
                name: "PK_flag",
                table: "flag",
                column: "Id");

            migrationBuilder.CreateTable(
                name: "submission",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ExamId = table.Column<long>(type: "bigint", nullable: false),
                    ExamStudentId = table.Column<long>(type: "bigint", nullable: false),
                    Attempt = table.Column<int>(type: "integer", nullable: false),
                    OriginalFileName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    OriginalFileUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    SourceFormat = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    FailureReason = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_submission", x => x.Id);
                    table.ForeignKey(
                        name: "FK_submission_exam_ExamId",
                        column: x => x.ExamId,
                        principalTable: "exam",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_submission_exam_student_ExamStudentId",
                        column: x => x.ExamStudentId,
                        principalTable: "exam_student",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.Sql("""
                ALTER TABLE submission ADD COLUMN "LegacyQuestionPacketId" bigint;

                INSERT INTO submission
                (
                    "ExamId",
                    "ExamStudentId",
                    "Attempt",
                    "OriginalFileName",
                    "OriginalFileUrl",
                    "SourceFormat",
                    "Status",
                    "FailureReason",
                    "CreatedAt",
                    "UpdatedAt",
                    "LegacyQuestionPacketId"
                )
                SELECT
                    ez."ExamId",
                    df."ExamStudentId",
                    1,
                    df."FileName",
                    df."FilePath",
                    NULL,
                    'Processed',
                    NULL,
                    NOW(),
                    NOW(),
                    qp."Id"
                FROM question_packet qp
                INNER JOIN doc_file df ON df."Id" = qp."SubmissionId"
                INNER JOIN exam_zip ez ON ez."Id" = df."ExamZipId";

                UPDATE question_packet qp
                SET "SubmissionId" = s."Id"
                FROM submission s
                WHERE s."LegacyQuestionPacketId" = qp."Id";

                ALTER TABLE submission DROP COLUMN "LegacyQuestionPacketId";
                """);

            migrationBuilder.CreateTable(
                name: "processing_job",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SubmissionId = table.Column<long>(type: "bigint", nullable: false),
                    JobType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ProgressPercent = table.Column<int>(type: "integer", nullable: false),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    ErrorMessage = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FinishedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_processing_job", x => x.Id);
                    table.ForeignKey(
                        name: "FK_processing_job_submission_SubmissionId",
                        column: x => x.SubmissionId,
                        principalTable: "submission",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_processing_job_SubmissionId",
                table: "processing_job",
                column: "SubmissionId");

            migrationBuilder.CreateIndex(
                name: "IX_submission_ExamId",
                table: "submission",
                column: "ExamId");

            migrationBuilder.CreateIndex(
                name: "IX_submission_ExamStudentId",
                table: "submission",
                column: "ExamStudentId");

            migrationBuilder.AddForeignKey(
                name: "FK_flag_Users_ReviewedByUserId",
                table: "flag",
                column: "ReviewedByUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_flag_question_packet_MatchedQuestionPacketId",
                table: "flag",
                column: "MatchedQuestionPacketId",
                principalTable: "question_packet",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_flag_question_packet_QuestionPacketId",
                table: "flag",
                column: "QuestionPacketId",
                principalTable: "question_packet",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_question_packet_exam_question_ExamQuestionId",
                table: "question_packet",
                column: "ExamQuestionId",
                principalTable: "exam_question",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_question_packet_submission_SubmissionId",
                table: "question_packet",
                column: "SubmissionId",
                principalTable: "submission",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_flag_Users_ReviewedByUserId",
                table: "flag");

            migrationBuilder.DropForeignKey(
                name: "FK_flag_question_packet_MatchedQuestionPacketId",
                table: "flag");

            migrationBuilder.DropForeignKey(
                name: "FK_flag_question_packet_QuestionPacketId",
                table: "flag");

            migrationBuilder.DropForeignKey(
                name: "FK_question_packet_exam_question_ExamQuestionId",
                table: "question_packet");

            migrationBuilder.DropForeignKey(
                name: "FK_question_packet_submission_SubmissionId",
                table: "question_packet");

            migrationBuilder.DropTable(
                name: "processing_job");

            migrationBuilder.DropTable(
                name: "submission");

            migrationBuilder.DropPrimaryKey(
                name: "PK_flag",
                table: "flag");

            migrationBuilder.DropColumn(
                name: "ImageUrisJson",
                table: "question_packet");

            migrationBuilder.DropColumn(
                name: "QuestionNumber",
                table: "doc_file");

            migrationBuilder.DropColumn(
                name: "TeacherDecision",
                table: "flag");

            migrationBuilder.DropColumn(
                name: "TeacherNotes",
                table: "flag");

            migrationBuilder.RenameTable(
                name: "flag",
                newName: "similarity_flag");

            migrationBuilder.RenameColumn(
                name: "SubmissionId",
                table: "question_packet",
                newName: "DocFileId");

            migrationBuilder.RenameColumn(
                name: "Status",
                table: "question_packet",
                newName: "PacketStatus");

            migrationBuilder.RenameColumn(
                name: "PrimaryImageUrl",
                table: "question_packet",
                newName: "ImageUrl");

            migrationBuilder.RenameColumn(
                name: "ExtractedAnswerText",
                table: "question_packet",
                newName: "ExtractedText");

            migrationBuilder.RenameColumn(
                name: "ParseNotes",
                table: "question_packet",
                newName: "Notes");

            migrationBuilder.RenameColumn(
                name: "ParseConfidence",
                table: "question_packet",
                newName: "Confidence");

            migrationBuilder.RenameIndex(
                name: "IX_question_packet_SubmissionId",
                table: "question_packet",
                newName: "IX_question_packet_DocFileId");

            migrationBuilder.RenameColumn(
                name: "ThresholdUsed",
                table: "similarity_flag",
                newName: "Threshold");

            migrationBuilder.RenameIndex(
                name: "IX_flag_ReviewStatus_Source",
                table: "similarity_flag",
                newName: "IX_similarity_flag_ReviewStatus_Source");

            migrationBuilder.RenameIndex(
                name: "IX_flag_ReviewedByUserId",
                table: "similarity_flag",
                newName: "IX_similarity_flag_ReviewedByUserId");

            migrationBuilder.RenameIndex(
                name: "IX_flag_QuestionPacketId_MatchedQuestionPacketId_Source",
                table: "similarity_flag",
                newName: "IX_similarity_flag_QuestionPacketId_MatchedQuestionPacketId_So~");

            migrationBuilder.RenameIndex(
                name: "IX_flag_MatchedQuestionPacketId",
                table: "similarity_flag",
                newName: "IX_similarity_flag_MatchedQuestionPacketId");

            migrationBuilder.AlterColumn<long>(
                name: "ExamQuestionId",
                table: "question_packet",
                type: "bigint",
                nullable: true,
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AlterColumn<string>(
                name: "Source",
                table: "similarity_flag",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100);

            migrationBuilder.AlterColumn<string>(
                name: "Notes",
                table: "question_packet",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "Confidence",
                table: "question_packet",
                type: "numeric(5,4)",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(5,2)",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "ReviewStatus",
                table: "similarity_flag",
                type: "integer",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20);

            migrationBuilder.AddColumn<string>(
                name: "AIVerificationResult",
                table: "similarity_flag",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "AIVerifiedAt",
                table: "similarity_flag",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Notes",
                table: "similarity_flag",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "TeacherScore",
                table: "similarity_flag",
                type: "numeric(5,2)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "similarity_flag",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddPrimaryKey(
                name: "PK_similarity_flag",
                table: "similarity_flag",
                column: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_question_packet_doc_file_DocFileId",
                table: "question_packet",
                column: "DocFileId",
                principalTable: "doc_file",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_question_packet_exam_question_ExamQuestionId",
                table: "question_packet",
                column: "ExamQuestionId",
                principalTable: "exam_question",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_similarity_flag_Users_ReviewedByUserId",
                table: "similarity_flag",
                column: "ReviewedByUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_similarity_flag_question_packet_MatchedQuestionPacketId",
                table: "similarity_flag",
                column: "MatchedQuestionPacketId",
                principalTable: "question_packet",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_similarity_flag_question_packet_QuestionPacketId",
                table: "similarity_flag",
                column: "QuestionPacketId",
                principalTable: "question_packet",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
