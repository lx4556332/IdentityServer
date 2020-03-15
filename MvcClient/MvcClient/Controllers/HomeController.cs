using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using IdentityModel.Client;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using MvcClient.Models;

namespace MvcClient.Controllers
{
    [Authorize]
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;

        public HomeController(ILogger<HomeController> logger)
        {
            _logger = logger;
        }

        public async Task<IActionResult> Index()
        {
            //return View();
            var client = new HttpClient();
            var disco = await client.GetDiscoveryDocumentAsync("http://localhost:5000/");
            if (disco.IsError)
            {
                throw new Exception(disco.Error);
            }

            var accessToken = await HttpContext.GetTokenAsync(OpenIdConnectParameterNames.AccessToken);

            client.SetBearerToken(accessToken);
            var response = await client.GetAsync("http://localhost:5001/identity");
            if (!response.IsSuccessStatusCode)
            {
                if (response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    await RenewTokensAsync();
                }

                throw new Exception(response.ReasonPhrase);
            }

            var content = await response.Content.ReadAsStringAsync();

            return View("Index", content);
        }

        public async Task<IActionResult> Privacy()
        {
            var accessToken = await HttpContext.GetTokenAsync(OpenIdConnectParameterNames.AccessToken);
            var IdToken = await HttpContext.GetTokenAsync(OpenIdConnectParameterNames.IdToken);
            var refreshToken=await HttpContext.GetTokenAsync(OpenIdConnectParameterNames.RefreshToken);
            //var authorizationCode=await HttpContext.GetTokenAsync(OpenIdConnectParameterNames.Code);


            ViewData["accessToken"] = accessToken;
            ViewData["idToken"] = IdToken;
            ViewData["refreshToken"] = accessToken;

            return View();
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }

        /// <summary>
        /// 退出
        /// </summary>
        /// <returns></returns>
        public async Task Logout()
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            await HttpContext.SignOutAsync(OpenIdConnectDefaults.AuthenticationScheme);
        }


        public async Task<IActionResult> RenewTokensAsync()
        {
            //先从nuget添加IdentityModel框架
            var client = new HttpClient();
            var authorizationServerInfo = await client.GetDiscoveryDocumentAsync("http://localhost:5000");
            if (authorizationServerInfo.IsError)
            {
                Console.WriteLine(authorizationServerInfo.Error);
                return Content(authorizationServerInfo.Error);
            }
            var tokenResponse = await client.RequestRefreshTokenAsync(new RefreshTokenRequest
            {
                Address = authorizationServerInfo.TokenEndpoint,
                ClientId = "mvc client",
                ClientSecret = "mvc secret",
                Scope = "api1 openid profile email phone address offline_access",
                RefreshToken = await HttpContext.GetTokenAsync("refresh_token")
            });
            if (tokenResponse.IsError)
            {
                Console.WriteLine(tokenResponse.Error);
                return Content(tokenResponse.Error);
            }
            var identityToken = await HttpContext.GetTokenAsync("id_token");
            var expiresAt = DateTime.UtcNow + TimeSpan.FromSeconds(tokenResponse.ExpiresIn);
            var tokens = new[]
            {
                new AuthenticationToken
                {
                    Name = OpenIdConnectParameterNames.IdToken,
                    Value = identityToken
                },
                new AuthenticationToken
                {
                    Name = OpenIdConnectParameterNames.AccessToken,
                    Value = tokenResponse.AccessToken
                },
                new AuthenticationToken
                {
                    Name = OpenIdConnectParameterNames.RefreshToken,
                    Value = tokenResponse.RefreshToken
                },
                new AuthenticationToken
                {
                    Name = "expires_at",
                    Value = expiresAt.ToString("o", CultureInfo.InvariantCulture)
                }
            };
            var authenticationInfo = await HttpContext.AuthenticateAsync("Cookies");
            authenticationInfo.Properties.StoreTokens(tokens);
            await HttpContext.SignInAsync("Cookies", authenticationInfo.Principal, authenticationInfo.Properties);

            return View("Privacy");
        }


        //Token 刷新
        private async Task<string> RenewTokensAsync1()
        {
            var client = new HttpClient();
            var disco = await client.GetDiscoveryDocumentAsync("http://localhost:5000");
            if (disco.IsError)
            {
                throw new Exception(disco.Error);
            }

            var refreshToken = await HttpContext.GetTokenAsync(OpenIdConnectParameterNames.RefreshToken);

            // Refresh Access Token
            var tokenResponse = await client.RequestRefreshTokenAsync(new RefreshTokenRequest
            {
                Address = disco.TokenEndpoint,
                ClientId = "mvc client",
                ClientSecret = "mvc secret",
                Scope = "api1 openid profile email phone address offline_access",
                GrantType = OpenIdConnectGrantTypes.RefreshToken,
                RefreshToken = refreshToken
            });

            if (tokenResponse.IsError)
            {
                throw new Exception(tokenResponse.Error);
            }

            var expiresAt = DateTime.UtcNow + TimeSpan.FromSeconds(tokenResponse.ExpiresIn);

            var tokens = new[]
            {
                new AuthenticationToken
                {
                    Name = OpenIdConnectParameterNames.IdToken,
                    Value = tokenResponse.IdentityToken
                },
                new AuthenticationToken
                {
                    Name = OpenIdConnectParameterNames.AccessToken,
                    Value = tokenResponse.AccessToken
                },
                new AuthenticationToken
                {
                    Name = OpenIdConnectParameterNames.RefreshToken,
                    Value = tokenResponse.RefreshToken
                },
                new AuthenticationToken
                {
                    Name = "expires_at",
                    Value = expiresAt.ToString("o", CultureInfo.InvariantCulture)
                }
            };

            // 获取身份认证的结果，包含当前的pricipal和properties
            var currentAuthenticateResult =
                await HttpContext.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);

            // 把新的tokens存起来
            currentAuthenticateResult.Properties.StoreTokens(tokens);

            // 登录
            await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
                currentAuthenticateResult.Principal, currentAuthenticateResult.Properties);

            return tokenResponse.AccessToken;
        }
    }
}
