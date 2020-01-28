﻿using Come.CollectiveOAuth.Cache;
using Come.CollectiveOAuth.Config;
using Come.CollectiveOAuth.Enums;
using Come.CollectiveOAuth.Models;
using Come.CollectiveOAuth.Utils;
using System;
using System.Collections.Generic;

namespace Come.CollectiveOAuth.Request
{
    public class XiaoMiAuthRequest : DefaultAuthRequest
    {
        private static readonly string PREFIX = "&&&START&&&";
        public XiaoMiAuthRequest(ClientConfig config) : base(config, new XiaoMiAuthSource())
        {
        }

        public XiaoMiAuthRequest(ClientConfig config, IAuthStateCache authStateCache)
            : base(config, new XiaoMiAuthSource(), authStateCache)
        {
        }

        protected override AuthToken getAccessToken(AuthCallback authCallback)
        {
            return getToken(accessTokenUrl(authCallback.code));
        }

        private AuthToken getToken(string accessTokenUrl)
        {
            string response = HttpUtils.RequestGet(accessTokenUrl);
            string jsonStr = response.Replace(PREFIX, "");
            var accessTokenObject = jsonStr.parseObject();

            if (accessTokenObject.ContainsKey("error"))
            {
                throw new Exception(accessTokenObject.GetParamString("error_description"));
            }

            var authToken = new AuthToken();
            authToken.accessToken = accessTokenObject.GetParamString("access_token");
            authToken.refreshToken = accessTokenObject.GetParamString("refresh_token");
            authToken.tokenType = accessTokenObject.GetParamString("token_type");
            authToken.expireIn = accessTokenObject.GetParamInt32("expires_in").Value;
            authToken.scope = accessTokenObject.GetParamString("scope");

            authToken.openId = accessTokenObject.GetParamString("openId");
            authToken.macAlgorithm = accessTokenObject.GetParamString("mac_algorithm");
            authToken.macKey = accessTokenObject.GetParamString("mac_key");

            return authToken;
        }

        protected override AuthUser getUserInfo(AuthToken authToken)
        {
            // 获取用户信息
            string userResponse = doGetUserInfo(authToken);

            var userProfile = userResponse.parseObject();
            if ("error".Equals(userProfile.GetParamString("result"), StringComparison.OrdinalIgnoreCase))
            {
                throw new Exception(userProfile.GetParamString("description"));
            }

            var userObj = userProfile.GetParamString("data").parseObject();

            var authUser = new AuthUser();
            authUser.uuid = userObj.GetParamString("id");
            authUser.username = userObj.GetParamString("miliaoNick");
            authUser.nickname = userObj.GetParamString("miliaoNick");
            authUser.avatar = userObj.GetParamString("miliaoIcon");
            authUser.email = userObj.GetParamString("mail");
            authUser.gender = AuthUserGender.UNKNOWN;

            authUser.token = authToken;
            authUser.source = source.getName();
            authUser.originalUser = userObj;
            authUser.originalUserStr = userResponse;
            //return authUser;

            // 获取用户邮箱手机号等信息
            string emailPhoneUrl = $"{{https://open.account.xiaomi.com/user/phoneAndEmail}}?clientId={config.clientId}&token={authToken.accessToken}";

            string emailResponse = HttpUtils.RequestGet(emailPhoneUrl);
            var userEmailPhone = emailResponse.parseObject();
            if (!"error".Equals(userEmailPhone.GetParamString("result"), StringComparison.OrdinalIgnoreCase))
            {
                var emailPhone = userEmailPhone.GetParamString("data").parseObject();
                authUser.email = emailPhone.GetParamString("email");
            }
            else
            {
                //Log.warn("小米开发平台暂时不对外开放用户手机及邮箱信息的获取");
            }

            return authUser;
        }

        /**
         * 刷新access token （续期）
         *
         * @param authToken 登录成功后返回的Token信息
         * @return AuthResponse
         */
        public override AuthResponse refresh(AuthToken authToken)
        {
            var token = getToken(refreshTokenUrl(authToken.refreshToken));
            return new AuthResponse(AuthResponseStatus.SUCCESS.GetCode(), AuthResponseStatus.SUCCESS.GetDesc(), token);
        }

        /**
         * 返回带{@code state}参数的授权url，授权回调时会带上这个{@code state}
         *
         * @param state state 验证授权流程的参数，可以防止csrf
         * @return 返回授权地址
         * @since 1.9.3
         */
        public override String authorize(String state)
        {
            return UrlBuilder.fromBaseUrl(source.authorize())
                .queryParam("response_type", "code")
                .queryParam("client_id", config.clientId)
                .queryParam("redirect_uri", config.redirectUri)
                .queryParam("scope", config.scope.IsNullOrWhiteSpace() ? "user/profile%20user/openIdV2%20user/phoneAndEmail" : config.scope)
                .queryParam("skip_confirm", "false")
                .queryParam("state", getRealState(state))
                .build();
        }

        /**
         * 返回获取userInfo的url
         *
         * @param authToken 用户授权后的token
         * @return 返回获取userInfo的url
         */
        protected override string userInfoUrl(AuthToken authToken)
        {
            return UrlBuilder.fromBaseUrl(source.userInfo())
                .queryParam("clientId", config.clientId)
                .queryParam("token", authToken.accessToken)
                .build();
        }

    }
}