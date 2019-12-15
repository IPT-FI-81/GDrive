using System;
using System.Threading;
using System.Threading.Tasks;
using Bijector.GDrive.Configs;
using Bijector.GDrive.Models;
using Bijector.Infrastructure.Repositories;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Drive.v3;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Bijector.GDrive.Services
{
    public class GoogleAuthService : IGoogleAuthService
    {
        private readonly IRepository<Token> tokenRepository;

        private readonly GoogleConfigs googleOptions;        

        public GoogleAuthService(IRepository<Token> tokenRepository, IOptions<GoogleConfigs> googleOptions)
        {
            this.tokenRepository = tokenRepository;
            this.googleOptions = googleOptions.Value;            
        }

        public AuthorizationCodeFlow GetAuthorizationCodeFlow()
        {
            var secretPath = googleOptions.SecretsFilePath;
            using(var stream = new System.IO.FileStream(secretPath, System.IO.FileMode.Open))
            {                
                var secrets = GoogleClientSecrets.Load(stream).Secrets;
                string[] scopes = new[] { DriveService.Scope.Drive };
                var initializer = new GoogleAuthorizationCodeFlow.Initializer { ClientSecrets = secrets, Scopes = scopes };
                var googleAuthorizationCodeFlow = new GoogleAuthorizationCodeFlow(initializer);
                return googleAuthorizationCodeFlow;
            }
        }

        public string GetAuthorizationRequestUrl()
        {
            //"https://localhost:5008/gdriveauth/getauthtoken";
            var redirectUrl = googleOptions.RedirectUrl;
            var googleAuthorizationCodeFlow = GetAuthorizationCodeFlow();            
            var codeRequestUrl = googleAuthorizationCodeFlow.CreateAuthorizationCodeRequest(redirectUrl);
            codeRequestUrl.ResponseType = "code";            
            var authorizationUrl = codeRequestUrl.Build();
            return authorizationUrl.AbsoluteUri;
        }

        public async Task<GoogleCredential> GetCredentialAsync(Guid serviceId)
        {
            var token = await tokenRepository.GetByIdAsync(serviceId);
            if(token != null)
            {
                if(!token.IsExpired(Google.Apis.Util.SystemClock.Default))
                {
                    return GoogleCredential.FromAccessToken(token.AccessToken);
                }
                else
                {
                    return await GetRefreshedCredentialAsync(serviceId);
                }
            } 
            return null;           
        }

        public async Task<GoogleCredential> GetRefreshedCredentialAsync(Guid serviceId)
        {
            var token = await tokenRepository.GetByIdAsync(serviceId);
            var authFlow = GetAuthorizationCodeFlow();
            var gtoken = await authFlow.RefreshTokenAsync(token.AccountId.ToString(), token.RefreshToken, CancellationToken.None);
            var newToken = GetTokenFromResponseMethod(gtoken);
            newToken.Id = token.Id;
            newToken.AccountId = token.AccountId;
            await tokenRepository.UpdateAsync(serviceId, newToken);
            return GoogleCredential.FromAccessToken(newToken.AccessToken);
        }

        public async Task<Token> StoreTokenFromCode(Guid accountId, string code)
        {
            var redirectUrl = googleOptions.RedirectUrl;
            
            var scopes = new[] { DriveService.Scope.Drive };
            var googleAuthorizationCodeFlow = GetAuthorizationCodeFlow();
            var gtoken = await googleAuthorizationCodeFlow.ExchangeCodeForTokenAsync(accountId.ToString(), code, redirectUrl, CancellationToken.None);

            if(gtoken != null)
            {                                
                var newToken = GetTokenFromResponseMethod(gtoken);
                newToken.AccountId = accountId;
                await tokenRepository.AddAsync(newToken);
                return await tokenRepository.FindAsync(t => t.AccessToken == gtoken.AccessToken && t.AccountId == accountId);
            }
            return null;
        }

        private Token GetTokenFromResponseMethod(TokenResponse gtoken)
        {
            var token = new Token
            {
                IdToken = gtoken.IdToken,
                AccessToken = gtoken.AccessToken,
                RefreshToken = gtoken.RefreshToken,
                Scope = gtoken.Scope,
                Issued = gtoken.Issued,
                IssuedUtc = gtoken.IssuedUtc,
                TokenType = gtoken.TokenType,
                ExpiresInSeconds = gtoken.ExpiresInSeconds.HasValue ? gtoken.ExpiresInSeconds : long.MaxValue
            };
            return token;
        }
    }
}