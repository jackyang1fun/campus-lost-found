using LostAndFound.App.Features.Claims;
using LostAndFound.App.Features.Listings;

namespace LostAndFound.Desktop.Shared;

public static class UiFormat
{
    public static string PostingTypeLabel(PostingType type) =>
        type == PostingType.Lost ? "失物" : "招领";

    public static string PostingStatusLabel(PostingStatus status) =>
        status switch
        {
            PostingStatus.PendingReview => "待审核",
            PostingStatus.Approved => "已通过",
            PostingStatus.Rejected => "已驳回",
            PostingStatus.Resolved => "已解决",
            PostingStatus.Cancelled => "已撤销",
            PostingStatus.Deleted => "已删除",
            _ => status.ToString()
        };

    public static string ClaimStatusLabel(ClaimStatus status) =>
        status switch
        {
            ClaimStatus.Negotiating => "协商中",
            ClaimStatus.Completed => "已完成",
            ClaimStatus.Cancelled => "已取消",
            ClaimStatus.Disputed => "争议中",
            ClaimStatus.DisputeClosed => "争议已关闭",
            _ => status.ToString()
        };

    public static string RelativeDate(DateTimeOffset value)
    {
        var local = value.ToLocalTime();
        var elapsed = DateTimeOffset.Now - local;
        if (elapsed.TotalMinutes < 1)
        {
            return "刚刚";
        }

        if (elapsed.TotalHours < 1)
        {
            return $"{(int)elapsed.TotalMinutes} 分钟前";
        }

        if (elapsed.TotalDays < 1)
        {
            return $"{(int)elapsed.TotalHours} 小时前";
        }

        if (elapsed.TotalDays < 7)
        {
            return $"{(int)elapsed.TotalDays} 天前";
        }

        return local.ToString("yyyy-MM-dd");
    }

    public static string AuditAction(string action) =>
        action switch
        {
            "UserRegistered" => "用户注册",
            "AdministratorCreated" => "初始化管理员",
            "ProfileUpdated" => "更新个人资料",
            "PasswordChanged" => "修改密码",
            "AccountDeactivated" => "注销账号",
            "PostingSubmitted" => "提交信息",
            "PostingApproved" => "审核通过",
            "PostingRejected" => "驳回信息",
            "PostingCanceledByPublisher" or "PostingCancelledByPublisher" => "发布者撤销",
            "PostingResolvedByPublisher" => "发布者标记解决",
            "PostingDeletedByAdministrator" => "管理员删除信息",
            "ClaimStarted" => "发起认领",
            "ClaimCompleted" => "完成认领",
            "ClaimCancelled" => "取消认领",
            "DisputeOpened" => "提交争议",
            "DisputeSensitiveAccess" => "查看争议敏感信息",
            "DisputeNeedMoreInfo" => "要求补充说明",
            "DisputeClosed" => "关闭争议",
            "ManualBackupCreated" => "创建手动备份",
            "AutomaticBackupCreated" => "创建自动备份",
            "BackupRestored" => "恢复数据库",
            "UserReactivated" => "恢复用户账号",
            "UserStatusChanged" => "变更用户状态",
            _ => action
        };
}
