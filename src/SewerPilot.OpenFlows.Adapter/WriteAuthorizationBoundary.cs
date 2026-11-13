using System;
using System.IO;

namespace SewerPilot.OpenFlows.Adapter
{
    public sealed class WriteAuthorizationToken
    {
        public WriteAuthorizationToken(
            string tokenId,
            string workingCopyId,
            string sourceBaselineFingerprint,
            string targetWorkingPath,
            string authorizedOperationScope,
            DateTimeOffset issuedUtc)
        {
            if (string.IsNullOrWhiteSpace(tokenId))
                throw new ArgumentException("Token ID is required.", nameof(tokenId));
            if (string.IsNullOrWhiteSpace(workingCopyId))
                throw new ArgumentException("Working copy ID is required.", nameof(workingCopyId));
            if (string.IsNullOrWhiteSpace(sourceBaselineFingerprint))
                throw new ArgumentException("Source baseline fingerprint is required.", nameof(sourceBaselineFingerprint));
            if (string.IsNullOrWhiteSpace(targetWorkingPath))
                throw new ArgumentException("Target working path is required.", nameof(targetWorkingPath));
            if (string.IsNullOrWhiteSpace(authorizedOperationScope))
                throw new ArgumentException("Authorized operation scope is required.", nameof(authorizedOperationScope));

            TokenId = tokenId;
            WorkingCopyId = workingCopyId;
            SourceBaselineFingerprint = sourceBaselineFingerprint;
            TargetWorkingPath = targetWorkingPath;
            AuthorizedOperationScope = authorizedOperationScope;
            IssuedUtc = issuedUtc;
        }

        public string TokenId { get; }
        public string WorkingCopyId { get; }
        public string SourceBaselineFingerprint { get; }
        public string TargetWorkingPath { get; }
        public string AuthorizedOperationScope { get; }
        public DateTimeOffset IssuedUtc { get; }

        public override string ToString() =>
            $"WriteAuthToken[{TokenId}:WorkingCopy={WorkingCopyId}:Scope={AuthorizedOperationScope}]";
    }

    public sealed class WriteAuthorizationDecision
    {
        private WriteAuthorizationDecision(bool isAuthorized, string rejectionReason, WriteAuthorizationToken token)
        {
            IsAuthorized = isAuthorized;
            RejectionReason = rejectionReason;
            Token = token;
        }

        public bool IsAuthorized { get; }
        public string RejectionReason { get; }
        public WriteAuthorizationToken Token { get; }

        public static WriteAuthorizationDecision Denied(string reason) =>
            new WriteAuthorizationDecision(false, reason ?? "Write authorization denied.", null);

        public static WriteAuthorizationDecision Granted(WriteAuthorizationToken token) =>
            new WriteAuthorizationDecision(true, null, token ?? throw new ArgumentNullException(nameof(token)));

        public override string ToString() =>
            IsAuthorized ? $"AUTHORIZED: {Token}" : $"DENIED: {RejectionReason}";
    }

    public static class WriteAuthorizationBoundary
    {
        public static WriteAuthorizationDecision RequestAuthorization(
            ProtectedBaseline baseline,
            ControlledWorkingCopy workingCopy,
            string operationScope)
        {
            if (baseline == null)
                return WriteAuthorizationDecision.Denied("DENIED: Protected baseline is null.");

            if (workingCopy == null)
                return WriteAuthorizationDecision.Denied("DENIED: Working copy is null.");

            if (string.IsNullOrWhiteSpace(operationScope))
                return WriteAuthorizationDecision.Denied("DENIED: Explicit operation scope is required.");

            try
            {
                SafePathPolicy.ValidateWorkingCopyLocation(baseline.DirectoryPath, workingCopy.DirectoryPath);
            }
            catch (Exception ex)
            {
                return WriteAuthorizationDecision.Denied($"DENIED: Path policy violation: {ex.Message}");
            }

            if (!string.Equals(workingCopy.Identity.SourceBaselineFingerprint, baseline.Fingerprint.Value, StringComparison.Ordinal))
            {
                return WriteAuthorizationDecision.Denied($"DENIED: Working copy source baseline fingerprint '{workingCopy.Identity.SourceBaselineFingerprint}' does not match registered baseline '{baseline.Fingerprint.Value}'.");
            }

            var baselineIntegrity = baseline.VerifyCurrentIntegrity();
            if (!baselineIntegrity.IsMatch)
            {
                return WriteAuthorizationDecision.Denied($"DENIED: Protected baseline integrity check failed: {baselineIntegrity}");
            }

            var workingIntegrity = workingCopy.VerifyCurrentIntegrity();
            if (!workingIntegrity.IsMatch)
            {
                return WriteAuthorizationDecision.Denied($"DENIED: Working copy integrity check failed: {workingIntegrity}");
            }

            if (!Directory.Exists(workingCopy.DirectoryPath) || !File.Exists(workingCopy.PrimaryFilePath))
            {
                return WriteAuthorizationDecision.Denied("DENIED: Working copy directory or primary file does not exist on disk.");
            }

            var token = new WriteAuthorizationToken(
                Guid.NewGuid().ToString("N"),
                workingCopy.Identity.WorkingCopyId,
                baseline.Fingerprint.Value,
                workingCopy.PrimaryFilePath,
                operationScope.Trim(),
                DateTimeOffset.UtcNow);

            return WriteAuthorizationDecision.Granted(token);
        }
    }
}
