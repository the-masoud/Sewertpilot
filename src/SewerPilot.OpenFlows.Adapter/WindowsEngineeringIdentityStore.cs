using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;

namespace SewerPilot.OpenFlows.Adapter
{
    // Authority-owned storage only. Protection against a different OS principal does not establish
    // independence from a client running with this same principal. No live verifier is composed here.
    public sealed class WindowsEngineeringIdentityStore
    {
        private readonly string directory, authoritySid;
        internal string DataPath => Path.Combine(directory, "authority.dpapi");
        public WindowsEngineeringIdentityStore(string existingProtectedDirectory, string expectedAuthoritySid)
        {
            directory = Path.GetFullPath(existingProtectedDirectory);
            authoritySid = expectedAuthoritySid;
            IdentityGuard.Require(IdentityGuard.ValidSid(authoritySid) && WindowsIdentity.GetCurrent().User.Value == authoritySid, "AUTHORITY_OS_IDENTITY_MISMATCH");
            CheckDirectory();
        }
        // Explicit installer/authority enrollment action; never called by authentication or requests.
        public static void ProvisionDirectory(string newDirectory, string expectedAuthoritySid)
        {
            IdentityGuard.Require(IdentityGuard.ValidSid(expectedAuthoritySid) && WindowsIdentity.GetCurrent().User.Value == expectedAuthoritySid, "BOOTSTRAP_OS_IDENTITY_DENIED");
            string full = Path.GetFullPath(newDirectory);
            IdentityGuard.Require(!Directory.Exists(full) && Directory.Exists(Path.GetDirectoryName(full)), "STORE_PROVISIONING_CONFLICT");
            CheckParents(Path.GetDirectoryName(full), expectedAuthoritySid);
            var sid = new SecurityIdentifier(expectedAuthoritySid);
            var security = new DirectorySecurity(); security.SetOwner(sid); security.SetAccessRuleProtection(true, false);
            security.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            Directory.CreateDirectory(full, security);
            new WindowsEngineeringIdentityStore(full, expectedAuthoritySid).CheckDirectory();
        }
        private static void CheckParents(string path, string authority)
        {
            var parents = new DirectoryInfo(path);
            string system = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null).Value;
            string administrators = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null).Value;
            var unsafeRights = FileSystemRights.DeleteSubdirectoriesAndFiles | FileSystemRights.ChangePermissions | FileSystemRights.TakeOwnership;
            for (int depth = 0; parents != null && depth < 64; depth++, parents = parents.Parent)
            {
                IdentityGuard.Require((parents.Attributes & FileAttributes.ReparsePoint) == 0, "STORE_REPARSE_POINT");
                var security = parents.GetAccessControl();
                var rules = security.GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>();
                IdentityGuard.Require(!rules.Any(r => r.AccessControlType == AccessControlType.Allow &&
                    (r.PropagationFlags & PropagationFlags.InheritOnly) == 0 && r.IdentityReference.Value != authority &&
                    r.IdentityReference.Value != system && r.IdentityReference.Value != administrators &&
                    (r.FileSystemRights & unsafeRights) != 0), "STORE_PARENT_UNTRUSTED_MUTATION");
            }
            IdentityGuard.Require(parents == null, "STORE_PATH_DEPTH");
        }
        private void CheckAcl(FileSystemSecurity security)
        {
            IdentityGuard.Require(security.GetOwner(typeof(SecurityIdentifier)).Value == authoritySid, "STORE_OWNER_INVALID");
            var rules = security.GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>().ToArray();
            IdentityGuard.Require(rules.Length > 0 && rules.All(r => r.AccessControlType == AccessControlType.Allow &&
                r.IdentityReference.Value == authoritySid) && rules.Any(r => (r.FileSystemRights & FileSystemRights.FullControl) == FileSystemRights.FullControl), "STORE_ACL_INVALID");
        }
        private void CheckDirectory()
        {
            IdentityGuard.Require(Directory.Exists(directory), "STORE_NOT_PROVISIONED"); CheckParents(directory, authoritySid);
            var security = Directory.GetAccessControl(directory);
            IdentityGuard.Require(security.AreAccessRulesProtected, "STORE_ACL_INHERITED"); CheckAcl(security);
        }
        private void CheckFile(string path)
        {
            IdentityGuard.Require((File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0, "STORE_REPARSE_POINT");
            CheckAcl(File.GetAccessControl(path));
        }
        private static byte[] Protect(byte[] data, bool encrypt)
        {
            // Optional platform assembly, not a vendor binding. Use the framework's authenticated DPAPI envelope.
            var assembly = Assembly.Load("System.Security, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a");
            var scope = assembly.GetType("System.Security.Cryptography.DataProtectionScope", true);
            var type = assembly.GetType("System.Security.Cryptography.ProtectedData", true);
            return (byte[])type.GetMethod(encrypt ? "Protect" : "Unprotect", new[] { typeof(byte[]), typeof(byte[]), scope })
                .Invoke(null, new object[] { data, Encoding.UTF8.GetBytes("SewerPilot.EngineeringIdentity.v1"), Enum.Parse(scope, "CurrentUser") });
        }
        internal T Update<T>(Func<EngineeringIdentityState, T> operation)
        {
            try
            {
                CheckDirectory();
                string lockPath = Path.Combine(directory, "authority.lock");
                if (File.Exists(lockPath)) CheckFile(lockPath);
                using (var held = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
                {
                    CheckFile(lockPath);
                    EngineeringIdentityState state;
                    if (File.Exists(DataPath))
                    {
                        CheckFile(DataPath); IdentityGuard.Require(new FileInfo(DataPath).Length <= 16 * 1024 * 1024, "STORE_TOO_LARGE");
                        byte[] plain = Protect(File.ReadAllBytes(DataPath), false);
                        try { state = RuntimeHostProtocol.Serializer().Deserialize<EngineeringIdentityState>(Encoding.UTF8.GetString(plain)); }
                        finally { Array.Clear(plain, 0, plain.Length); }
                    }
                    else state = new EngineeringIdentityState();
                    IdentityGuard.Require(state != null && state.Schema == 1 && state.Accounts != null && state.Grants != null && state.Sessions != null && state.Approvals != null && state.Audit != null, "STORE_CORRUPT");
                    T value = operation(state); state.Revision = checked(state.Revision + 1);
                    byte[] bytes = Encoding.UTF8.GetBytes(RuntimeHostProtocol.Serializer().Serialize(state));
                    byte[] encrypted;
                    try { IdentityGuard.Require(bytes.Length <= 16 * 1024 * 1024, "STORE_TOO_LARGE"); encrypted = Protect(bytes, true); }
                    finally { Array.Clear(bytes, 0, bytes.Length); }
                    string temporary = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".tmp");
                    try
                    {
                        using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                        { output.Write(encrypted, 0, encrypted.Length); output.Flush(true); }
                        CheckFile(temporary);
                        if (File.Exists(DataPath)) File.Replace(temporary, DataPath, null);
                        else File.Move(temporary, DataPath);
                        CheckFile(DataPath);
                    }
                    finally { if (File.Exists(temporary)) File.Delete(temporary); }
                    return value;
                }
            }
            catch (EngineeringIdentityException) { throw; }
            catch { throw new EngineeringIdentityException("PROTECTED_IDENTITY_STORE_UNAVAILABLE_OR_CORRUPT"); }
        }
        public AuditRecord[] ReadAudit() => Update(s => s.Audit.ToArray());
    }
}
