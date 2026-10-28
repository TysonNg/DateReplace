namespace ContractDateReplacer.Core.Enums;

/// <summary>
/// Phân loại ngữ cảnh của ngày tháng trong hợp đồng.
/// </summary>
public enum DateClassificationType
{
    Unknown = 0,
    ContractSignDate,
    ContractEffectiveDate,
    ContractStartDate,
    ContractEndDate,
    BirthDate,
    IdentityIssueDate,
    IdentityExpiryDate,
    DocumentDate,
    PaymentDate,
    HandoverDate,
    AppendixDate,
    DisputeDate,
    Other
}
