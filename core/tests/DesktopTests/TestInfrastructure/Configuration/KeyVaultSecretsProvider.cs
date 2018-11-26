﻿using System;
using System.Threading.Tasks;
using Microsoft.Azure.KeyVault;
using Microsoft.Azure.KeyVault.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Clients.ActiveDirectory;

namespace Microsoft.Identity.AutomationTests.Configuration
{
    internal static class KeyVaultSecretsProvider
    {
        /// <summary>
        /// Token cache used by the test infrastructure when authenticating against KeyVault
        /// </summary>
        /// <remarks>We aren't using the default cache to make sure the tokens used by this
        /// test infrastructure can't end up in the cache being used by the tests (the UI-less
        /// Desktop test app runs in the same AppDomain as the infrastructure and uses the
        /// default cache).</remarks>
        private static readonly TokenCache keyVaultTokenCache = new TokenCache();

        private static KeyVaultClient _keyVaultClient;

        private static ClientAssertionCertificate _assertionCert;

        private static KeyVaultConfiguration _config;

        /// <summary>Initialize the secrets provider with the "keyVault" configuration section.</summary>
        /// <remarks>
        /// <para>
        /// Authentication using <see cref="KeyVaultAuthenticationType.ClientCertificate"/>
        ///     1. Register Azure AD application of "Web app / API" type.
        ///        To set up certificate based access to the application PowerShell should be used.
        ///     2. Add an access policy entry to target Key Vault instance for this application.
        ///
        ///     The "keyVault" configuration section should define:
        ///         "authType": "ClientCertificate"
        ///         "clientId": [client ID]
        ///         "certThumbprint": [certificate thumbprint]
        /// </para>
        /// <para>
        /// Authentication using <see cref="KeyVaultAuthenticationType.UserCredential"/>
        ///     1. Register Azure AD application of "Native" type.
        ///     2. Add to 'Required permissions' access to 'Azure Key Vault (AzureKeyVault)' API.
        ///     3. When you run your native client application, it will automatically prompt user to enter Azure AD credentials.
        ///     4. To successfully access keys/secrets in the Key Vault, the user must have specific permissions to perform those operations.
        ///        This could be achieved by directly adding an access policy entry to target Key Vault instance for this user
        ///        or an access policy entry for an Azure AD security group of which this user is a member of.
        ///
        ///     The "keyVault" configuration section should define:
        ///         "authType": "UserCredential"
        ///         "clientId": [client ID]
        /// </para>
        /// </remarks>
        /// <param name="section">Configuration section for Key Vault</param>
        public static void Initialize(IConfigurationSection section)
        {
            if (section == null)
            {
                throw new ArgumentNullException(nameof(section));
            }

            _config = new KeyVaultConfiguration(section);
            _keyVaultClient = new KeyVaultClient(AuthenticationCallbackAsync);
        }

        public static SecretBundle GetSecret(string secretUrl)
        {
            return _keyVaultClient.GetSecretAsync(secretUrl).GetAwaiter().GetResult();
        }

        private static async Task<string> AuthenticationCallbackAsync(string authority, string resource, string scope)
        {
            var authContext = new AuthenticationContext(authority, keyVaultTokenCache);

            AuthenticationResult authResult;
            switch (_config.AuthType)
            {
                case KeyVaultAuthenticationType.ClientCertificate:
                    if (_assertionCert == null)
                    {
                        var cert = CertificateHelper.FindCertificateByThumbprint(_config.CertThumbprint);
                        _assertionCert = new ClientAssertionCertificate(_config.ClientId, cert);
                    }
                    authResult = await authContext.AcquireTokenAsync(resource, _assertionCert);
                    break;
                case KeyVaultAuthenticationType.UserCredential:
                    authResult = await authContext.AcquireTokenAsync(resource, _config.ClientId, new UserCredential());
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            return authResult?.AccessToken;
        }
    }
}
