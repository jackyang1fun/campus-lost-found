using System.Globalization;
using LostAndFound.App.Domain;
using LostAndFound.App.Infrastructure;
using Microsoft.Data.Sqlite;

namespace LostAndFound.App.Features.Listings;

public sealed class ListingService
{
    private readonly SqliteDatabase _database;
    private readonly LocalImageStore _imageStore;
    private readonly AuditService _auditService;

    public ListingService(
        SqliteDatabase database,
        LocalImageStore imageStore,
        AuditService auditService)
    {
        _database = database;
        _imageStore = imageStore;
        _auditService = auditService;
    }

    public IReadOnlyList<Category> GetCategories()
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, Name
            FROM Categories
            WHERE IsEnabled = 1
            ORDER BY Id;
            """;

        using var reader = command.ExecuteReader();
        var categories = new List<Category>();
        while (reader.Read())
        {
            categories.Add(new Category(
                reader.GetInt32(reader.GetOrdinal("Id")),
                reader.GetString(reader.GetOrdinal("Name"))));
        }

        return categories;
    }

    public ListingResult CreatePosting(UserAccount publisher, CreatePostingRequest request)
    {
        var validationMessage = ValidatePosting(request);
        if (validationMessage is not null)
        {
            return ListingResult.Failure(validationMessage);
        }

        string? storedImagePath = null;
        try
        {
            if (!string.IsNullOrWhiteSpace(request.ImageSourcePath))
            {
                storedImagePath = _imageStore.Save(request.ImageSourcePath);
            }

            using var connection = _database.OpenConnection();
            if (!CategoryExists(connection, request.CategoryId))
            {
                return ListingResult.Failure("所选物品分类不存在或已停用。");
            }

            using var transaction = connection.BeginTransaction();
            var now = DateTimeOffset.UtcNow;
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO Postings (
                    Type,
                    Title,
                    CategoryId,
                    OccurredAtUtc,
                    Location,
                    Description,
                    ImagePath,
                    ImageAltText,
                    Contact,
                    PublisherUserId,
                    Status,
                    CreatedAtUtc,
                    UpdatedAtUtc)
                VALUES (
                    $type,
                    $title,
                    $categoryId,
                    $occurredAtUtc,
                    $location,
                    $description,
                    $imagePath,
                    $imageAltText,
                    $contact,
                    $publisherUserId,
                    'PendingReview',
                    $createdAtUtc,
                    $updatedAtUtc);
                SELECT last_insert_rowid();
                """;
            command.Parameters.AddWithValue("$type", request.Type.ToString());
            command.Parameters.AddWithValue("$title", request.Title.Trim());
            command.Parameters.AddWithValue("$categoryId", request.CategoryId);
            command.Parameters.AddWithValue("$occurredAtUtc", request.OccurredAt.ToUniversalTime().ToString("O"));
            command.Parameters.AddWithValue("$location", request.Location.Trim());
            command.Parameters.AddWithValue("$description", request.Description.Trim());
            command.Parameters.AddWithValue("$imagePath", (object?)storedImagePath ?? DBNull.Value);
            command.Parameters.AddWithValue(
                "$imageAltText",
                string.IsNullOrWhiteSpace(request.ImageAltText)
                    ? DBNull.Value
                    : request.ImageAltText.Trim());
            command.Parameters.AddWithValue(
                "$contact",
                string.IsNullOrWhiteSpace(request.Contact)
                    ? publisher.Contact
                    : request.Contact.Trim());
            command.Parameters.AddWithValue("$publisherUserId", publisher.Id);
            command.Parameters.AddWithValue("$createdAtUtc", now.ToString("O"));
            command.Parameters.AddWithValue("$updatedAtUtc", now.ToString("O"));

            var postingId = Convert.ToInt64(command.ExecuteScalar());
            _auditService.Write(
                connection,
                transaction,
                publisher.Id,
                "PostingSubmitted",
                "Posting",
                postingId.ToString(CultureInfo.InvariantCulture),
                null,
                now);

            transaction.Commit();
            return ListingResult.Success("信息已提交，等待管理员审核。", postingId);
        }
        catch (Exception exception) when (
            exception is IOException
            or InvalidOperationException
            or UnauthorizedAccessException)
        {
            _imageStore.Delete(storedImagePath);
            return ListingResult.Failure(exception.Message);
        }
    }

    public IReadOnlyList<PostingSummary> GetPublicPostings(string? searchTerm, int? categoryId)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = BuildPostingQuery(publicOnly: true, publisherId: null);

        AddSearchParameters(command, searchTerm, categoryId);
        return ReadPostings(command);
    }

    public IReadOnlyList<PostingSummary> GetOwnPostings(long publisherId)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = BuildPostingQuery(publicOnly: false, publisherId: publisherId);
        command.Parameters.AddWithValue("$publisherId", publisherId);
        AddSearchParameters(command, searchTerm: null, categoryId: null);
        return ReadPostings(command);
    }

    public IReadOnlyList<PostingSummary> GetPendingPostings(UserAccount administrator)
    {
        if (administrator.Role != UserRole.Administrator || administrator.Status != UserStatus.Active)
        {
            return [];
        }

        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                p.Id,
                p.Type,
                p.Title,
                p.CategoryId,
                c.Name AS CategoryName,
                p.OccurredAtUtc,
                p.Location,
                p.Description,
                p.ImagePath,
                p.ImageAltText,
                p.Contact,
                p.PublisherUserId,
                u.DisplayName AS PublisherName,
                p.Status,
                p.ReviewReason,
                p.CreatedAtUtc
            FROM Postings p
            JOIN Categories c ON c.Id = p.CategoryId
            JOIN Users u ON u.Id = p.PublisherUserId
            WHERE p.Status = 'PendingReview'
            ORDER BY p.CreatedAtUtc;
            """;
        return ReadPostings(command);
    }

    public IReadOnlyList<PostingSummary> GetAllPostings(UserAccount administrator)
    {
        if (administrator.Role != UserRole.Administrator ||
            administrator.Status != UserStatus.Active)
        {
            return [];
        }

        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                p.Id,
                p.Type,
                p.Title,
                p.CategoryId,
                c.Name AS CategoryName,
                p.OccurredAtUtc,
                p.Location,
                p.Description,
                p.ImagePath,
                p.ImageAltText,
                p.Contact,
                p.PublisherUserId,
                u.DisplayName AS PublisherName,
                p.Status,
                p.ReviewReason,
                p.CreatedAtUtc
            FROM Postings p
            JOIN Categories c ON c.Id = p.CategoryId
            JOIN Users u ON u.Id = p.PublisherUserId
            ORDER BY p.UpdatedAtUtc DESC
            LIMIT 500;
            """;
        return ReadPostings(command);
    }

    public ListingResult ReviewPosting(
        UserAccount administrator,
        long postingId,
        bool approve,
        string? reason)
    {
        if (administrator.Role != UserRole.Administrator || administrator.Status != UserStatus.Active)
        {
            return ListingResult.Failure("只有管理员可以审核信息。");
        }

        if (!approve && string.IsNullOrWhiteSpace(reason))
        {
            return ListingResult.Failure("驳回信息时必须填写驳回理由。");
        }

        using var connection = _database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        var now = DateTimeOffset.UtcNow;
        var newStatus = approve ? PostingStatus.Approved : PostingStatus.Rejected;

        using (var update = connection.CreateCommand())
        {
            update.Transaction = transaction;
            update.CommandText = """
                UPDATE Postings
                SET
                    Status = $status,
                    ReviewReason = $reason,
                    Version = Version + 1,
                    UpdatedAtUtc = $updatedAtUtc
                WHERE Id = $postingId
                  AND Status = 'PendingReview';
                """;
            update.Parameters.AddWithValue("$status", newStatus.ToString());
            update.Parameters.AddWithValue(
                "$reason",
                string.IsNullOrWhiteSpace(reason) ? DBNull.Value : reason.Trim());
            update.Parameters.AddWithValue("$updatedAtUtc", now.ToString("O"));
            update.Parameters.AddWithValue("$postingId", postingId);

            if (update.ExecuteNonQuery() != 1)
            {
                transaction.Rollback();
                return ListingResult.Failure("该信息不存在或已被其他管理员处理。");
            }
        }

        using (var reviewLog = connection.CreateCommand())
        {
            reviewLog.Transaction = transaction;
            reviewLog.CommandText = """
                INSERT INTO PostingReviewLogs (
                    PostingId,
                    AdminUserId,
                    Action,
                    Reason,
                    CreatedAtUtc)
                VALUES (
                    $postingId,
                    $adminUserId,
                    $action,
                    $reason,
                    $createdAtUtc);
                """;
            reviewLog.Parameters.AddWithValue("$postingId", postingId);
            reviewLog.Parameters.AddWithValue("$adminUserId", administrator.Id);
            reviewLog.Parameters.AddWithValue("$action", newStatus.ToString());
            reviewLog.Parameters.AddWithValue(
                "$reason",
                string.IsNullOrWhiteSpace(reason) ? DBNull.Value : reason.Trim());
            reviewLog.Parameters.AddWithValue("$createdAtUtc", now.ToString("O"));
            reviewLog.ExecuteNonQuery();
        }

        _auditService.Write(
            connection,
            transaction,
            administrator.Id,
            approve ? "PostingApproved" : "PostingRejected",
            "Posting",
            postingId.ToString(CultureInfo.InvariantCulture),
            reason,
            now);

        transaction.Commit();
        return ListingResult.Success(approve ? "信息审核通过。" : "信息已驳回。");
    }

    public ListingResult CancelOwnPosting(UserAccount publisher, long postingId)
    {
        using var connection = _database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        var now = DateTimeOffset.UtcNow;

        using (var update = connection.CreateCommand())
        {
            update.Transaction = transaction;
            update.CommandText = """
                UPDATE Postings
                SET
                    Status = 'Cancelled',
                    Version = Version + 1,
                    UpdatedAtUtc = $now
                WHERE Id = $postingId
                  AND PublisherUserId = $publisherId
                  AND Status IN ('PendingReview', 'Approved', 'Rejected');
                """;
            update.Parameters.AddWithValue("$now", now.ToString("O"));
            update.Parameters.AddWithValue("$postingId", postingId);
            update.Parameters.AddWithValue("$publisherId", publisher.Id);
            if (update.ExecuteNonQuery() != 1)
            {
                transaction.Rollback();
                return ListingResult.Failure("该信息无法撤销。");
            }
        }

        _auditService.Write(
            connection,
            transaction,
            publisher.Id,
            "PostingCancelledByPublisher",
            "Posting",
            postingId.ToString(CultureInfo.InvariantCulture),
            null,
            now);
        transaction.Commit();
        return ListingResult.Success("信息已撤销。");
    }

    public ListingResult UpdateOwnPosting(
        UserAccount publisher,
        long postingId,
        CreatePostingRequest request,
        bool removeImage)
    {
        var validationMessage = ValidatePosting(request);
        if (validationMessage is not null)
        {
            return ListingResult.Failure(validationMessage);
        }

        string? newImagePath = null;
        string? previousImagePath = null;

        try
        {
            using var connection = _database.OpenConnection();
            if (!CategoryExists(connection, request.CategoryId))
            {
                return ListingResult.Failure("所选物品分类不存在或已停用。");
            }

            using (var read = connection.CreateCommand())
            {
                read.CommandText = """
                    SELECT ImagePath, Status
                    FROM Postings
                    WHERE Id = $postingId
                      AND PublisherUserId = $publisherId
                    LIMIT 1;
                    """;
                read.Parameters.AddWithValue("$postingId", postingId);
                read.Parameters.AddWithValue("$publisherId", publisher.Id);
                using var reader = read.ExecuteReader();
                if (!reader.Read())
                {
                    return ListingResult.Failure("信息不存在或不属于当前用户。");
                }

                var status = Enum.Parse<PostingStatus>(
                    reader.GetString(reader.GetOrdinal("Status")));
                if (status is PostingStatus.Resolved or PostingStatus.Cancelled or PostingStatus.Deleted)
                {
                    return ListingResult.Failure("当前状态不能编辑信息。");
                }

                previousImagePath = reader.IsDBNull(reader.GetOrdinal("ImagePath"))
                    ? null
                    : reader.GetString(reader.GetOrdinal("ImagePath"));
            }

            if (!string.IsNullOrWhiteSpace(request.ImageSourcePath))
            {
                newImagePath = _imageStore.Save(request.ImageSourcePath);
            }

            var effectiveImagePath = removeImage
                ? null
                : newImagePath ?? previousImagePath;
            var effectiveAltText = effectiveImagePath is null
                ? null
                : request.ImageAltText?.Trim();
            if (effectiveImagePath is not null && string.IsNullOrWhiteSpace(effectiveAltText))
            {
                _imageStore.Delete(newImagePath);
                return ListingResult.Failure("上传图片时必须填写图片替代文本。");
            }

            using var transaction = connection.BeginTransaction();
            var now = DateTimeOffset.UtcNow;
            using var update = connection.CreateCommand();
            update.Transaction = transaction;
            update.CommandText = """
                UPDATE Postings
                SET
                    Type = $type,
                    Title = $title,
                    CategoryId = $categoryId,
                    OccurredAtUtc = $occurredAtUtc,
                    Location = $location,
                    Description = $description,
                    ImagePath = $imagePath,
                    ImageAltText = $imageAltText,
                    Contact = $contact,
                    Status = 'PendingReview',
                    ReviewReason = NULL,
                    Version = Version + 1,
                    UpdatedAtUtc = $updatedAtUtc
                WHERE Id = $postingId
                  AND PublisherUserId = $publisherId
                  AND Status IN ('PendingReview', 'Approved', 'Rejected');
                """;
            update.Parameters.AddWithValue("$type", request.Type.ToString());
            update.Parameters.AddWithValue("$title", request.Title.Trim());
            update.Parameters.AddWithValue("$categoryId", request.CategoryId);
            update.Parameters.AddWithValue("$occurredAtUtc", request.OccurredAt.ToUniversalTime().ToString("O"));
            update.Parameters.AddWithValue("$location", request.Location.Trim());
            update.Parameters.AddWithValue("$description", request.Description.Trim());
            update.Parameters.AddWithValue("$imagePath", (object?)effectiveImagePath ?? DBNull.Value);
            update.Parameters.AddWithValue(
                "$imageAltText",
                string.IsNullOrWhiteSpace(effectiveAltText) ? DBNull.Value : effectiveAltText);
            update.Parameters.AddWithValue("$contact", request.Contact.Trim());
            update.Parameters.AddWithValue("$updatedAtUtc", now.ToString("O"));
            update.Parameters.AddWithValue("$postingId", postingId);
            update.Parameters.AddWithValue("$publisherId", publisher.Id);

            if (update.ExecuteNonQuery() != 1)
            {
                transaction.Rollback();
                _imageStore.Delete(newImagePath);
                return ListingResult.Failure("信息状态已变化，无法保存修改。");
            }

            _auditService.Write(
                connection,
                transaction,
                publisher.Id,
                "PostingUpdated",
                "Posting",
                postingId.ToString(CultureInfo.InvariantCulture),
                null,
                now);
            transaction.Commit();

            if ((removeImage || newImagePath is not null) &&
                previousImagePath is not null &&
                !string.Equals(previousImagePath, newImagePath, StringComparison.Ordinal))
            {
                _imageStore.Delete(previousImagePath);
            }

            return ListingResult.Success("信息已更新并重新提交审核。", postingId);
        }
        catch (Exception exception) when (
            exception is IOException
            or InvalidOperationException
            or UnauthorizedAccessException
            or SqliteException)
        {
            _imageStore.Delete(newImagePath);
            return ListingResult.Failure(exception.Message);
        }
    }

    public ListingResult MarkOwnResolved(UserAccount publisher, long postingId)
    {
        using var connection = _database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        var now = DateTimeOffset.UtcNow;

        using (var update = connection.CreateCommand())
        {
            update.Transaction = transaction;
            update.CommandText = """
                UPDATE Postings
                SET
                    Status = 'Resolved',
                    Version = Version + 1,
                    UpdatedAtUtc = $now
                WHERE Id = $postingId
                  AND PublisherUserId = $publisherId
                  AND Status = 'Approved';
                """;
            update.Parameters.AddWithValue("$now", now.ToString("O"));
            update.Parameters.AddWithValue("$postingId", postingId);
            update.Parameters.AddWithValue("$publisherId", publisher.Id);
            if (update.ExecuteNonQuery() != 1)
            {
                transaction.Rollback();
                return ListingResult.Failure("只有已通过审核的信息可以标记为解决。");
            }
        }

        _auditService.Write(
            connection,
            transaction,
            publisher.Id,
            "PostingResolvedByPublisher",
            "Posting",
            postingId.ToString(CultureInfo.InvariantCulture),
            null,
            now);
        transaction.Commit();
        return ListingResult.Success("信息已标记为已解决。");
    }

    public ListingResult DeletePosting(
        UserAccount administrator,
        long postingId,
        string reason)
    {
        if (administrator.Role != UserRole.Administrator)
        {
            return ListingResult.Failure("只有管理员可以删除信息。");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            return ListingResult.Failure("删除信息时必须填写理由。");
        }

        using var connection = _database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        var now = DateTimeOffset.UtcNow;

        using (var update = connection.CreateCommand())
        {
            update.Transaction = transaction;
            update.CommandText = """
                UPDATE Postings
                SET
                    Status = 'Deleted',
                    ReviewReason = $reason,
                    Version = Version + 1,
                    UpdatedAtUtc = $now
                WHERE Id = $postingId
                  AND Status <> 'Deleted';
                """;
            update.Parameters.AddWithValue("$reason", reason.Trim());
            update.Parameters.AddWithValue("$now", now.ToString("O"));
            update.Parameters.AddWithValue("$postingId", postingId);
            if (update.ExecuteNonQuery() != 1)
            {
                transaction.Rollback();
                return ListingResult.Failure("该信息不存在或已经删除。");
            }
        }

        using (var reviewLog = connection.CreateCommand())
        {
            reviewLog.Transaction = transaction;
            reviewLog.CommandText = """
                INSERT INTO PostingReviewLogs (
                    PostingId, AdminUserId, Action, Reason, CreatedAtUtc)
                VALUES (
                    $postingId, $adminUserId, 'Deleted', $reason, $now);
                """;
            reviewLog.Parameters.AddWithValue("$postingId", postingId);
            reviewLog.Parameters.AddWithValue("$adminUserId", administrator.Id);
            reviewLog.Parameters.AddWithValue("$reason", reason.Trim());
            reviewLog.Parameters.AddWithValue("$now", now.ToString("O"));
            reviewLog.ExecuteNonQuery();
        }

        _auditService.Write(
            connection,
            transaction,
            administrator.Id,
            "PostingDeletedByAdministrator",
            "Posting",
            postingId.ToString(CultureInfo.InvariantCulture),
            reason,
            now);
        transaction.Commit();
        return ListingResult.Success("信息已删除并保留审计记录。");
    }

    private static string BuildPostingQuery(bool publicOnly, long? publisherId)
    {
        var filter = publicOnly
            ? "p.Status = 'Approved'"
            : "p.Status <> 'Deleted' AND p.PublisherUserId = $publisherId";

        return $"""
            SELECT
                p.Id,
                p.Type,
                p.Title,
                p.CategoryId,
                c.Name AS CategoryName,
                p.OccurredAtUtc,
                p.Location,
                p.Description,
                p.ImagePath,
                p.ImageAltText,
                p.Contact,
                p.PublisherUserId,
                u.DisplayName AS PublisherName,
                p.Status,
                p.ReviewReason,
                p.CreatedAtUtc
            FROM Postings p
            JOIN Categories c ON c.Id = p.CategoryId
            JOIN Users u ON u.Id = p.PublisherUserId
            WHERE {filter}
              AND ($searchTerm IS NULL
                   OR p.Title LIKE $searchTerm
                   OR p.Description LIKE $searchTerm
                   OR p.Location LIKE $searchTerm)
              AND ($categoryId IS NULL OR p.CategoryId = $categoryId)
            ORDER BY p.CreatedAtUtc DESC
            LIMIT 200;
            """;
    }

    private static void AddSearchParameters(
        SqliteCommand command,
        string? searchTerm,
        int? categoryId)
    {
        command.Parameters.AddWithValue(
            "$searchTerm",
            string.IsNullOrWhiteSpace(searchTerm) ? DBNull.Value : $"%{searchTerm.Trim()}%");
        command.Parameters.AddWithValue(
            "$categoryId",
            categoryId.HasValue ? categoryId.Value : DBNull.Value);
    }

    private static IReadOnlyList<PostingSummary> ReadPostings(SqliteCommand command)
    {
        using var reader = command.ExecuteReader();
        var postings = new List<PostingSummary>();

        while (reader.Read())
        {
            postings.Add(new PostingSummary(
                reader.GetInt64(reader.GetOrdinal("Id")),
                Enum.Parse<PostingType>(reader.GetString(reader.GetOrdinal("Type"))),
                reader.GetString(reader.GetOrdinal("Title")),
                reader.GetInt32(reader.GetOrdinal("CategoryId")),
                reader.GetString(reader.GetOrdinal("CategoryName")),
                ParseTimestamp(reader.GetString(reader.GetOrdinal("OccurredAtUtc"))),
                reader.GetString(reader.GetOrdinal("Location")),
                reader.GetString(reader.GetOrdinal("Description")),
                reader.IsDBNull(reader.GetOrdinal("ImagePath"))
                    ? null
                    : reader.GetString(reader.GetOrdinal("ImagePath")),
                reader.IsDBNull(reader.GetOrdinal("ImageAltText"))
                    ? null
                    : reader.GetString(reader.GetOrdinal("ImageAltText")),
                reader.GetString(reader.GetOrdinal("Contact")),
                reader.GetInt64(reader.GetOrdinal("PublisherUserId")),
                reader.GetString(reader.GetOrdinal("PublisherName")),
                Enum.Parse<PostingStatus>(reader.GetString(reader.GetOrdinal("Status"))),
                reader.IsDBNull(reader.GetOrdinal("ReviewReason"))
                    ? null
                    : reader.GetString(reader.GetOrdinal("ReviewReason")),
                ParseTimestamp(reader.GetString(reader.GetOrdinal("CreatedAtUtc")))));
        }

        return postings;
    }

    private static DateTimeOffset ParseTimestamp(string value) =>
        DateTimeOffset.Parse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind);

    private static bool CategoryExists(SqliteConnection connection, int categoryId)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT EXISTS(
                SELECT 1
                FROM Categories
                WHERE Id = $categoryId
                  AND IsEnabled = 1);
            """;
        command.Parameters.AddWithValue("$categoryId", categoryId);
        return Convert.ToInt64(command.ExecuteScalar()) == 1;
    }

    private static string? ValidatePosting(CreatePostingRequest request)
    {
        if (!Enum.IsDefined(request.Type))
        {
            return "信息类型无效。";
        }

        if (request.Title.Trim().Length is < 2 or > 100)
        {
            return "物品名称长度必须为 2 至 100 个字符。";
        }

        if (request.CategoryId <= 0)
        {
            return "请选择物品分类。";
        }

        if (request.Location.Trim().Length is < 1 or > 100)
        {
            return "地点长度必须为 1 至 100 个字符。";
        }

        if (request.Description.Trim().Length is < 1 or > 1_000)
        {
            return "物品描述长度必须为 1 至 1000 个字符。";
        }

        if (string.IsNullOrWhiteSpace(request.ImageSourcePath))
        {
            return null;
        }

        return string.IsNullOrWhiteSpace(request.ImageAltText)
            ? "上传图片时必须填写图片替代文本。"
            : null;
    }
}
