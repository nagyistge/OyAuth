﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace SimpleAuth {
    public static class OAuth {
        private const string _UnreservedChars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-_.~";
        public const string HMACSHA1 = "HMAC-SHA1";

        private static readonly TimeSpan MaxNonceAge = TimeSpan.FromMinutes(5);
        private static readonly ConcurrentDictionary<string, DateTime> _NonceCache = new ConcurrentDictionary<string, DateTime>();
        private static readonly System.Threading.Timer _Cleanup = new System.Threading.Timer(state => {
            lock (_Cleanup) {
                var keys = _NonceCache.Where(x => x.Value.Add(MaxNonceAge) < DateTime.UtcNow).Select(x => x.Key).ToArray();
                DateTime value;
                foreach (var key in keys)
                    _NonceCache.TryRemove(key, out value);
            }
        }, null, MaxNonceAge, MaxNonceAge);

        public static string GetNonce(int length = 20) {
            var random = new Random();
            string nonce;
            while (true) {
                nonce = string.Empty;
                while (nonce.Length < length) {
                    nonce += _UnreservedChars[random.Next(0, 63)];
                }

                if (!_NonceCache.ContainsKey(nonce)) {
                    _NonceCache.TryAdd(nonce, DateTime.UtcNow);
                    return nonce;
                }
            }
        }

        public static bool Validate(System.Web.HttpRequestBase request, double numSecondsValid, Func<string, string> GetConsumerSecret, bool throwOnError = false) {
            return Validate(request.HttpMethod, request.Url.ToString(), request.Form.ToString(), request.Headers["Authorization"], numSecondsValid, GetConsumerSecret, throwOnError);
        }
        public static bool Validate(System.Web.HttpRequest request, double numSecondsValid, Func<string, string> GetConsumerSecret, bool throwOnError = false) {
            return Validate(request.HttpMethod, request.Url.ToString(), request.Form.ToString(), request.Headers["Authorization"], numSecondsValid, GetConsumerSecret, throwOnError);
        }

        public static bool Validate(string url, Func<string, string> GetConsumerSecret) {
            return Validate(null, url, null, null, 90, GetConsumerSecret);
        }

        public static bool Validate(string method, string url, string posted, string authorizationHeader, double numSecondsValid, Func<string, string> GetConsumerSecret, bool throwOnError = false, Func<string, string, string> GetTokenSecret = null) {
            method = method ?? "GET";

            if (numSecondsValid < 0 || numSecondsValid >= MaxNonceAge.TotalSeconds)
                throw new ArgumentException(string.Format("Must be more than 0 and less than {0} seconds", MaxNonceAge.TotalSeconds), "numSecondsValid");

            var query = Utilities.ParseQueryString(url, posted);
            if (!authorizationHeader.IsNullOrEmpty()) {
                var authorization = ParseAuthorizationHeader(authorizationHeader);
                authorization.Keys.ForEach(key => query.SetValue(key, authorization[key]));
            }

            if (query.GetValue("oauth_version") != "1.0") {
                if (throwOnError) throw new System.Web.HttpException(401, "Invalid version specified");
            }

            if (numSecondsValid > 0) {
                double timestamp = query.GetValue("oauth_timestamp").ToDouble();
                double diff = Math.Abs(DateTime.UtcNow.GetSecondsSince1970() - timestamp);

                if (diff > numSecondsValid) {
                    if (throwOnError) throw new System.Web.HttpException(401, "The timestamp is too old");
                    return false;
                }

                DateTime used = _NonceCache[query.GetValue("oauth_nonce")];
                if (used.AddSeconds(numSecondsValid) > DateTime.UtcNow) {
                    if (throwOnError) throw new System.Web.HttpException(401, "The nonce is not unique");
                    return false;
                }
                _NonceCache[query.GetValue("oauth_nonce")] = DateTime.UtcNow;
            }

            string hashAlgorithm = query.GetValue("oauth_signature_method");
            int q = url.IndexOf('?');
            string path = q == -1 ? url : url.Substring(0, q);

            string secret = GetConsumerSecret(query.GetValue("oauth_consumer_key"));
            string sig;
            try {
                //var postedquery = ParseQueryString(string.Empty, posted);
                var querystring = GetQueryString(query, null, true);
                sig = GetSignature(method, hashAlgorithm, secret, path, querystring, GetTokenSecret != null && query.ContainsKey("oauth_token") ? GetTokenSecret(query["oauth_token"], query.GetValue("oauth_verifier")) : null);
            } catch (Exception) {
                if (throwOnError) throw;
                return false;
            }

            if (sig != query["oauth_signature"]) {
                if (throwOnError) throw new System.Web.HttpException(401, "The signature is invalid");
                return false;
            }

            return true;
        }

        public static IDictionary<string, string> ParseAuthorizationHeader(string header) {
            while (header.StartsWith("OAuth")) header = header.Substring(5).Trim();

            var result = new Dictionary<string, string>();
            while (header.Length > 0) {
                var eq = header.IndexOf('=');
                if (eq < 0) eq = header.Length;
                var name = header.Substring(0, eq).Trim().Trim(',').Trim();

                var value = header = header.Substring((eq + 1).AtMost(header.Length)).Trim();

                if (value.StartsWith("\"")) {
                    ProcessHeaderValue(1, ref header, ref value, '"');
                } else if (value.StartsWith("'")) {
                    ProcessHeaderValue(1, ref header, ref value, '\'');
                } else {
                    ProcessHeaderValue(0, ref header, ref value, ' ', ',');
                }

                result.SetValue(name, Uri.UnescapeDataString(value));
            }

            return result;
        }

        private static void ProcessHeaderValue(int skip, ref string header, ref string value, params char[] lookFor) {
            var quote = value.IndexOfAny(lookFor, skip);
            if (quote < 0) quote = value.Length;
            header = header.Substring((quote + 1).AtMost(header.Length));
            value = value.Substring(skip, quote - skip);
        }

        private static string GetQueryString(IEnumerable<KeyValuePair<string, string>> query, IDictionary<string, string> posted, bool encode) {
            return string.Join("&",
                query.OrderBy(x => x.Value).OrderBy(x => x.Key)
                    .Where(x => !x.Key.Is("oauth_signature") && (posted == null || !posted.ContainsKey(x.Key)))
                    .Select(x =>
                        string.Concat(x.Key, (x.Value == null ? string.Empty : "=" + (encode ? x.Value.UrlEncode() : x.Value)))).ToArray());
        }

        public static string GenerateUrl(string url, string consumerKey, string consumerSecret, string method = null, string hashAlgorithm = null, string posted = null, string token = null, string verifier = null, string tokenSecret = null) {
            var result = GetInfo(method, hashAlgorithm, ref url, posted, consumerKey, consumerSecret, token, verifier, tokenSecret);
            var querystring = GetQueryString(result.Item1, result.Item2, true);
            return string.Concat(url, "?", querystring, "&oauth_signature=", result.Item3.UrlEncode());
        }

        public static string GenerateAuthorizationHeader(string url, string consumerKey, string consumerSecret, string method = null, string hashAlgorithm = null, string posted = null, string token = null, string verifier = null, string tokenSecret = null, string realm = null) {
            var result = GetInfo(method, hashAlgorithm, ref url, posted, consumerKey, consumerSecret, token, verifier, tokenSecret);
            result.Item1.Add("oauth_signature", result.Item3);
            realm = realm.IsNullOrEmpty() ? url.ToUri().GetLeftPart(UriPartial.Authority) : realm;

            var @params = result.Item1.Where(x => x.Key.StartsWith("oauth_")).OrderBy(x => x.Key)
                .Select(x => string.Format("{0}=\"{1}\"", x.Key, x.Value.UrlEncode())).ToArray();
            return string.Concat("OAuth realm=\"", realm.UrlEncode(), "\", ", string.Join(", ", @params));
        }

        public static Tuple<IDictionary<string, string>, IDictionary<string, string>, string> GetInfo(string method, string hashAlgorithm, ref string url, string posted, string consumerKey, string consumerSecret, string token, string verifier, string tokenSecret) {
            method = method ?? "GET";
            hashAlgorithm = hashAlgorithm ?? HMACSHA1;

            string timestamp = DateTime.UtcNow.GetSecondsSince1970().ToString();
            string nonce = GetNonce();

            var query = Utilities.ParseQueryString(url, posted);
            var postedquery = Utilities.ParseQueryString(string.Empty, posted);

            int q = url.IndexOf('?');
            if (q > -1) url = url.Substring(0, q);

            //add the oauth stuffs
            query.SetValue("oauth_consumer_key", consumerKey);
            query.SetValue("oauth_nonce", nonce);
            query.SetValue("oauth_signature_method", hashAlgorithm);
            query.SetValue("oauth_timestamp", timestamp);
            query.SetValue("oauth_version", "1.0");
            if (token != null) query.SetValue("oauth_token", token);
            if (verifier != null) query.SetValue("oauth_verifier", verifier);

            //put the querystring back together in alphabetical order
            string querystring = GetQueryString(query, null, true);
            string sig = GetSignature(method, hashAlgorithm, consumerSecret, url, querystring, tokenSecret);

            return Tuple.Create(query, postedquery, sig);
        }

        private static string GetSignature(string method, string hashAlgorithm, string consumerSecret, string url, string querystring, string tokenSecret) {
            string data = string.Concat(method.ToUpper(), "&", url.UrlEncode(), "&", querystring.UrlEncode());

            string sig;
            using (var hasher = GetHashAglorithm(hashAlgorithm)) {
                hasher.Key = string.Concat(consumerSecret.UrlEncode(), "&", tokenSecret.IsNullOrEmpty() ? null : tokenSecret.UrlEncode()).GetBytes();
                sig = hasher.ComputeHash(data.GetBytes()).ToBase64();
            }

            return sig;
        }

        private static KeyedHashAlgorithm GetHashAglorithm(string name) {
            switch (name) {
                case HMACSHA1: return new HMACSHA1();
                default: throw new NotSupportedException(string.Format("The specified type, {0}, is not supported.", name));
            }
        }

        /// <summary>
        /// This is a different Url Encode implementation since the default .NET one outputs the percent encoding in lower case.
        /// While this is not a problem with the percent encoding spec, it is used in upper case throughout OAuth
        /// </summary>
        /// <param name="value">The value to Url encode</param>
        /// <returns>Returns a Url encoded string</returns>
        public static string UrlEncode(this string value) {
            if (value == null) return null;
            StringBuilder result = new StringBuilder();
            foreach (char symbol in value) {
                if (_UnreservedChars.IndexOf(symbol) != -1) {
                    result.Append(symbol);
                } else {
                    result.AppendFormat("%{0:X2}", (int)symbol);
                }
            }

            return result.ToString();
        }
    }
}