using System;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ContractDateReplacer.Core.Interfaces;
using ContractDateReplacer.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ContractDateReplacer.Infrastructure.Services;

/// <summary>
/// Dịch vụ thông báo Webhook tự động gửi sự kiện và dữ liệu nghiệm thu sang hệ thống thứ ba (ERP, DMS, CRM).
/// </summary>
public class WebhookNotificationService : IWebhookNotificationService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<WebhookNotificationService> _logger;

    public WebhookNotificationService(
        HttpClient? httpClient = null,
        ILogger<WebhookNotificationService>? logger = null)
    {
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        _logger = logger ?? NullLogger<WebhookNotificationService>.Instance;
    }

    public string GenerateHmacSignature(string payloadJson, string secretKey)
    {
        if (string.IsNullOrEmpty(secretKey))
        {
            return string.Empty;
        }

        byte[] keyBytes = Encoding.UTF8.GetBytes(secretKey);
        byte[] dataBytes = Encoding.UTF8.GetBytes(payloadJson);

        using var hmac = new HMACSHA256(keyBytes);
        byte[] hash = hmac.ComputeHash(dataBytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public async Task<WebhookDeliveryResult> SendBatchCompletedNotificationAsync(
        WebhookPayload payload,
        string webhookUrl,
        string secretKey,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(webhookUrl))
        {
            return new WebhookDeliveryResult
            {
                Success = false,
                ErrorMessage = "Đường dẫn Webhook URL không hợp lệ."
            };
        }

        string json = JsonSerializer.Serialize(payload);
        string signature = GenerateHmacSignature(json, secretKey);

        var result = new WebhookDeliveryResult
        {
            Signature = signature
        };

        const int maxRetries = 3;
        int delayMs = 500;

        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, webhookUrl)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json")
                };

                if (!string.IsNullOrEmpty(signature))
                {
                    request.Headers.Add("X-Signature", $"sha256={signature}");
                }

                request.Headers.Add("X-Batch-Id", payload.BatchId);
                request.Headers.Add("X-Event-Type", payload.EventType);
                request.Headers.Add("User-Agent", "Antigravity-ContractDateReplacer/1.0");

                var response = await _httpClient.SendAsync(request, cancellationToken);
                result.StatusCode = (int)response.StatusCode;

                if (response.IsSuccessStatusCode)
                {
                    result.Success = true;
                    result.RetryCount = attempt - 1;
                    _logger.LogInformation("Gửi Webhook thành công đến {Url} (Lần thử {Attempt}, HTTP {Code})",
                        webhookUrl, attempt, result.StatusCode);
                    return result;
                }

                result.ErrorMessage = $"HTTP {(int)response.StatusCode}: {response.ReasonPhrase}";
                _logger.LogWarning("Webhook phản hồi mã lỗi {Code} từ {Url} (Lần thử {Attempt})",
                    result.StatusCode, webhookUrl, attempt);
            }
            catch (Exception ex) when (attempt < maxRetries)
            {
                result.ErrorMessage = ex.Message;
                _logger.LogWarning(ex, "Lỗi kết nối Webhook lần {Attempt} tới {Url}. Thử lại sau {Delay}ms...",
                    attempt, webhookUrl, delayMs);

                await Task.Delay(delayMs, cancellationToken);
                delayMs *= 2; // Exponential backoff
            }
            catch (Exception ex)
            {
                result.ErrorMessage = ex.Message;
                result.RetryCount = attempt - 1;
                _logger.LogError(ex, "Không thể gửi Webhook tới {Url} sau {Attempt} lần thử.", webhookUrl, attempt);
                return result;
            }
        }

        result.RetryCount = maxRetries;
        return result;
    }
}
