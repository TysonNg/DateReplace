using System.Threading;
using System.Threading.Tasks;
using ContractDateReplacer.Core.Models;

namespace ContractDateReplacer.Core.Interfaces;

/// <summary>
/// Dịch vụ thông báo Webhook tự động gửi sự kiện và dữ liệu nghiệm thu sang hệ thống thứ ba (ERP, DMS, CRM).
/// </summary>
public interface IWebhookNotificationService
{
    /// <summary>
    /// Tạo chữ ký số HMAC-SHA256 bảo mật cho chuỗi Payload JSON với Secret Key.
    /// </summary>
    string GenerateHmacSignature(string payloadJson, string secretKey);

    /// <summary>
    /// Gửi thông báo Webhook HTTP POST kèm header X-Signature và cơ chế retry khi gặp lỗi mạng.
    /// </summary>
    Task<WebhookDeliveryResult> SendBatchCompletedNotificationAsync(
        WebhookPayload payload,
        string webhookUrl,
        string secretKey,
        CancellationToken cancellationToken = default);
}
