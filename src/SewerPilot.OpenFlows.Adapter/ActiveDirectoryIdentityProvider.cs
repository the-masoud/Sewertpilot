using System;
using System.Collections;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Security.Principal;
using System.Text;

namespace SewerPilot.OpenFlows.Adapter
{
    internal sealed class EngineeringDirectoryIdentity
    {
        internal string Sid;
        internal string[] SecurityGroups;
        internal bool Enabled, MembershipVerified;
    }
    internal interface IEngineeringDirectory
    {
        EngineeringDirectoryIdentity Authenticate(ActiveDirectoryIdentityConfiguration configuration, string username, string password);
        EngineeringDirectoryIdentity Revalidate(ActiveDirectoryIdentityConfiguration configuration, string sid);
    }
    // Framework LDAP is loaded on demand to preserve the Adapter's existing compile-time reference boundary.
    // All types/methods here are platform LDAP APIs, never vendor APIs or caller-selected types.
    internal sealed class WindowsLdapsEngineeringDirectory : IEngineeringDirectory
    {
        private static Assembly Platform() => Assembly.Load("System.DirectoryServices.Protocols, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a");
        private static Type Type(Assembly a, string name) => a.GetType("System.DirectoryServices.Protocols." + name, true);
        private static object Property(object value, string name) => value.GetType().GetProperty(name).GetValue(value, null);
        private static void Assign(object value, string name, object setting) => value.GetType().GetProperty(name).GetSetMethod().Invoke(value, new[] { setting });
        private static string Escape(string value)
        {
            var result = new StringBuilder();
            foreach (byte b in Encoding.UTF8.GetBytes(value)) result.Append("\\").Append(b.ToString("x2"));
            return result.ToString();
        }
        public EngineeringDirectoryIdentity Authenticate(ActiveDirectoryIdentityConfiguration c, string username, string password)
        {
            c.Validate(); IdentityGuard.Username(username);
            IdentityGuard.Require(!string.IsNullOrEmpty(password) && password.Length <= 1024, "AD_CREDENTIALS_INVALID");
            // Resolve a unique DN using the authority service's integrated Windows identity, then bind
            // that exact DN. UPN aliases/implicit UPN collision must never authenticate a different SID.
            string dn = ResolveUserDn(c, username);
            return Query(c, new NetworkCredential(dn, password), "Basic", "(objectClass=user)", dn, "Base");
        }
        public EngineeringDirectoryIdentity Revalidate(ActiveDirectoryIdentityConfiguration c, string sid)
        {
            c.Validate(); IdentityGuard.Require(IdentityGuard.ValidSid(sid), "AD_IDENTITY_INVALID");
            var bytes = new byte[new SecurityIdentifier(sid).BinaryLength]; new SecurityIdentifier(sid).GetBinaryForm(bytes, 0);
            string filter = "(&(objectClass=user)(objectSid=" + string.Concat(bytes.Select(b => "\\" + b.ToString("x2"))) + "))";
            // The future authority service's Windows token queries AD; no stored user password.
            return Query(c, CredentialCache.DefaultNetworkCredentials, "Negotiate", filter);
        }
        private static object Open(Assembly a, ActiveDirectoryIdentityConfiguration c, NetworkCredential credential, string authentication)
        {
            object identifier = Activator.CreateInstance(Type(a, "LdapDirectoryIdentifier"), new object[] { c.DomainController, c.Port, true, false });
            object connection = Activator.CreateInstance(Type(a, "LdapConnection"), new[] { identifier, credential, Enum.Parse(Type(a, "AuthType"), authentication) });
            try
            {
                var timeout = TimeSpan.FromSeconds(c.TimeoutSeconds); Assign(connection, "Timeout", timeout);
                object options = Property(connection, "SessionOptions");
                Assign(options, "ProtocolVersion", 3); Assign(options, "SecureSocketLayer", true);
                Assign(options, "SendTimeout", timeout); Assign(options, "ReferralChasing", Enum.Parse(Type(a, "ReferralChasingOptions"), "None"));
                // No VerifyServerCertificate callback: normal WinLDAP/Schannel chain and hostname checks.
                connection.GetType().GetMethod("Bind", System.Type.EmptyTypes).Invoke(connection, null);
                return connection;
            }
            catch { (connection as IDisposable)?.Dispose(); throw; }
        }
        private static string ResolveUserDn(ActiveDirectoryIdentityConfiguration c, string username)
        {
            object connection = null;
            try
            {
                var a = Platform(); connection = Open(a, c, CredentialCache.DefaultNetworkCredentials, "Negotiate");
                object response = Search(a, connection, c, c.SearchBase, "(&(objectClass=user)(sAMAccountName=" + Escape(username) + "))", "Subtree", new[] { "objectSid" });
                var entries = ((IEnumerable)Property(response, "Entries")).Cast<object>().ToArray();
                IdentityGuard.Require(entries.Length == 1, "AD_IDENTITY_UNCERTAIN");
                return (string)Property(entries[0], "DistinguishedName");
            }
            catch (EngineeringIdentityException) { throw; }
            catch { throw new EngineeringIdentityException("AD_AUTHENTICATION_OR_DIRECTORY_UNAVAILABLE"); }
            finally { (connection as IDisposable)?.Dispose(); }
        }
        private static EngineeringDirectoryIdentity Query(ActiveDirectoryIdentityConfiguration c, NetworkCredential credential, string authentication, string filter, string searchDn = null, string scope = "Subtree")
        {
            object connection = null;
            try
            {
                var a = Platform();
                connection = Open(a, c, credential, authentication);
                object found = Search(a, connection, c, searchDn ?? c.SearchBase, filter, scope, new[] { "objectSid", "userAccountControl" });
                var matches = ((IEnumerable)Property(found, "Entries")).Cast<object>().ToArray();
                IdentityGuard.Require(matches.Length == 1, "AD_IDENTITY_UNCERTAIN");
                object entry = matches[0];
                string dn = (string)Property(entry, "DistinguishedName");
                // tokenGroups is a constructed attribute: request it with a BASE query, never infer membership from names.
                object details = Search(a, connection, c, dn, "(objectClass=*)", "Base", new[] { "objectSid", "tokenGroups", "userAccountControl" });
                var bases = ((IEnumerable)Property(details, "Entries")).Cast<object>().ToArray();
                IdentityGuard.Require(bases.Length == 1, "AD_MEMBERSHIP_UNCERTAIN");
                object attrs = Property(bases[0], "Attributes");
                object sid = Attribute(attrs, "objectSid"), groups = Attribute(attrs, "tokenGroups"), uac = Attribute(attrs, "userAccountControl");
                IdentityGuard.Require(sid != null && groups != null && uac != null, "AD_MEMBERSHIP_UNCERTAIN");
                var sidBytes = Values(sid, typeof(byte[])); var groupBytes = Values(groups, typeof(byte[])); var flags = Values(uac, typeof(string));
                IdentityGuard.Require(sidBytes.Length == 1 && flags.Length == 1 && groupBytes.Length > 0, "AD_MEMBERSHIP_UNCERTAIN");
                var identity = new EngineeringDirectoryIdentity { Sid = new SecurityIdentifier((byte[])sidBytes[0], 0).Value,
                    SecurityGroups = groupBytes.Cast<byte[]>().Select(b => new SecurityIdentifier(b, 0).Value).ToArray(),
                    Enabled = (int.Parse((string)flags[0], System.Globalization.CultureInfo.InvariantCulture) & 2) == 0, MembershipVerified = true };
                return identity;
            }
            catch (EngineeringIdentityException) { throw; }
            catch { throw new EngineeringIdentityException("AD_AUTHENTICATION_OR_DIRECTORY_UNAVAILABLE"); }
            finally { (connection as IDisposable)?.Dispose(); }
        }
        private static object Search(Assembly a, object connection, ActiveDirectoryIdentityConfiguration c, string dn, string filter, string scope, string[] attributes)
        {
            object request = Activator.CreateInstance(Type(a, "SearchRequest"), new object[] { dn, filter, Enum.Parse(Type(a, "SearchScope"), scope), attributes });
            Assign(request, "TimeLimit", TimeSpan.FromSeconds(c.TimeoutSeconds)); Assign(request, "SizeLimit", 2);
            return connection.GetType().GetMethod("SendRequest", new[] { Type(a, "DirectoryRequest"), typeof(TimeSpan) })
                .Invoke(connection, new[] { request, (object)TimeSpan.FromSeconds(c.TimeoutSeconds) });
        }
        private static object Attribute(object attributes, string name) => attributes.GetType().GetProperty("Item", new[] { typeof(string) }).GetValue(attributes, new object[] { name });
        private static object[] Values(object attribute, Type type) => ((Array)attribute.GetType().GetMethod("GetValues").Invoke(attribute, new object[] { type })).Cast<object>().ToArray();
    }
}
