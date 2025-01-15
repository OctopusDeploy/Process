using System;
using System.DirectoryServices.AccountManagement;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace Tests.Plumbing;

class TestUserPrincipal
{
    public TestUserPrincipal(string username, string password = "Password01!")
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            throw new PlatformNotSupportedException();

        try
        {
            using (var principalContext = new PrincipalContext(ContextType.Machine))
            {
                UserPrincipal? principal = null;
                try
                {
                    principal = UserPrincipal.FindByIdentity(principalContext, IdentityType.Name, username);
                    if (principal != null)
                    {
                        Console.WriteLine($"The Windows User Account named '{username}' already exists, making sure the password is set correctly...");
                        principal.SetPassword(password);
                        principal.Save();
                    }
                    else
                    {
                        Console.WriteLine($"Trying to create a Windows User Account on the local machine called '{username}'...");
                        principal = new UserPrincipal(principalContext);
                        principal.Name = username;
                        principal.SetPassword(password);
                        principal.Save();
                    }

                    // Remember the pertinent details
                    SamAccountName = principal.SamAccountName;
                    Sid = principal.Sid;
                    Password = password;
                }
                finally
                {
                    principal?.Dispose();
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to find or create the Windows User Account called '{username}': {ex.Message}");
            throw;
        }
    }

    public SecurityIdentifier Sid { get; }

    public string NTAccountName => RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? Sid.Translate(typeof(NTAccount)).ToString() : throw new PlatformNotSupportedException();
    public string DomainName => NTAccountName.Split(new[] { '\\' }, 2)[0];
    public string UserName => NTAccountName.Split(new[] { '\\' }, 2)[1];
    public string SamAccountName { get; }
    public string Password { get; }

    public NetworkCredential GetCredential() => new(UserName, Password, DomainName);

    public override string ToString()
        => NTAccountName;
}