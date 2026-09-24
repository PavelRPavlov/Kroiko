namespace Kroiko.Domain;

// The end customer's contacts written into the order files. Either may be missing on the Server
// (a user profile without them); the PWA requires both before generating (ADR-0005).
public sealed record ContactInfo(string? CompanyName, string? MobileNumber);
