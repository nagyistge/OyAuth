﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace SimpleAuth {
    internal static class Utilities {
        private static DateTime _1970 = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        internal static Uri ToUri(this string url) {
            Uri uri = null;
            if (System.Uri.TryCreate(url, UriKind.Absolute, out uri)) return uri;
            return null;
        }

        internal static Uri ToUri(this string url, Uri @base) {
            if (url.IsNullOrEmpty()) return null;
            Uri uri = null;
            if (System.Uri.TryCreate(@base, url, out uri)) return uri;
            return null;
        }

        internal static string NotNull(this string input) {
            return input ?? string.Empty;
        }

        internal static string Left(this string input, int length) {
            if (input.IsNullOrEmpty()) return string.Empty;
            return input.Substring(0, input.Length.AtMost(length));
        }

        internal static byte[] GetBytes(this string input) {
            return System.Text.Encoding.ASCII.GetBytes(input);
        }

        internal static string ToBase64(this byte[] input) {
            return Convert.ToBase64String(input);
        }

        internal static int HexToInt(this string input, int defaultValue = 0) {
            int ret;
            if (int.TryParse(input, System.Globalization.NumberStyles.HexNumber, null, out ret))
                return ret;
            return defaultValue;
        }

        internal static bool Is(this string a, string b) {
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }

        internal static bool Between<T>(this T a, T b, T c) where T : IComparable<T> {
            int ab = a.CompareTo(b);
            if (ab == 0) return true;
            int ac = a.CompareTo(c);
            if (ac == 0) return true;
            if (ab + ac == 0) return true;
            return false;
        }

        internal static bool Contains<T>(this T bitmask, T flag) where T : struct, IConvertible {
            int v1 = Convert.ToInt32(bitmask);
            int v2 = Convert.ToInt32(flag);
            return (v1 & v2) == v2;
        }

        internal static T SetFlag<T>(this T bitmask, T flag, bool raise) where T : struct, IConvertible {
            if (raise) {
                object v1 = Convert.ToInt32(bitmask) | Convert.ToInt32(flag);
                return (T)v1;
            } else {
                return bitmask.Remove(flag);
            }
        }

        internal static T Remove<T>(this T bitmask, T flag) where T : struct, IConvertible {
            if (bitmask.Contains(flag)) {
                object v1 = Convert.ToInt32(bitmask) - Convert.ToInt32(flag);
                return (T)v1;
            } else {
                return bitmask;
            }
        }

        internal static T GetValue<T>(this IDictionary<string, T> dictionary, string name, T defaultValue = default(T)) {
            T value = default(T);
            if (dictionary.TryGetValue(name, out value))
                return value;
            return defaultValue;
        }

        internal static void SetValue<T>(this IDictionary<string, T> dictionary, string name, T value) {
            lock (dictionary) {
                if (!dictionary.ContainsKey(name)) {
                    dictionary.Add(name, value);
                } else {
                    dictionary[name] = value;
                }
            }
        }

        internal static string NotEmpty(this string input, string otherwise) {
            return input.IsNullOrEmpty() ? otherwise : input;
        }

        internal static string NotEmpty(this string input, params string[] otherwise) {
            return input.IsNullOrEmpty() ? otherwise.FirstOrDefault(x => !x.IsNullOrEmpty()) : input;
        }

        internal static bool Contains(this string input, string other, StringComparison comparison) {
            return input.NotNull().IndexOf(other, comparison) > -1;
        }

        internal static string Join<T>(this IEnumerable<T> input, string sep) {
            return string.Join(sep, input);
        }

        internal static bool IsNullOrEmpty(this IEnumerable items) {
            if (items == null) return true;
            if (items is Array) return ((Array)items).Length == 0;
            if (items is ICollection) return ((ICollection)items).Count == 0;
            if (items is string) return ((string)items).Length == 0;
            var e = items.GetEnumerator();
            try {
                return !e.MoveNext();
            } finally {
                if (e is IDisposable) ((IDisposable)e).Dispose();
                e = null;
            }
        }

        internal static IEnumerable<T> ForEach<T>(this IEnumerable<T> list, Action<T> forEach) {
            foreach (var item in list) forEach(item);
            return list;
        }


        internal static long GetSecondsSince1970(this DateTime datetime) {
            return (long)(datetime - _1970).TotalSeconds;
        }

        internal static bool IsNullOrEmpty(this string input) {
            return string.IsNullOrEmpty(input);
        }

        internal static double ToDouble(this string input, double @default = 0) {
            double d;
            if (double.TryParse(input, out d))
                return d;
            else return @default;
        }

        internal static int ToInt(this string input, int @default = 0) {
            int d;
            if (int.TryParse(input, out d))
                return d;
            else return @default;
        }

        internal static T AtMost<T>(this T input, params T[] maxs) where T : struct,IComparable<T> {
            var max = maxs.Min();
            if (input.CompareTo(max) == 1) return max;
            return input;
        }

        internal static IDictionary<string, string> ParseQueryString(string url, string querystring = null) {
            int index = url.IndexOf('?');
            querystring = querystring.NotNull();

            if (index > -1) {
                querystring = url.Substring(index + 1)
                    + (querystring.Length > 0 ? '&' + querystring : string.Empty);
            }

            //parse the querystring into a dictionary, rather than NameValueCollection, so it's easier to manipulate 
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (querystring.Length == 0) return result;

            var items = querystring.Split('&').Select(x => {
                int i = x.IndexOf('=');
                if (i == -1) return new[] { x, null };
                else return new[] { x.Substring(0, i), Uri.UnescapeDataString(x.Substring(i + 1)) };
            });
            foreach (var item in items) {
                if (result.ContainsKey(item[0]))
                    result[item[0]] += ',' + item[1];
                else result.Add(item[0], item[1]);
            }

            return result;
        }

    }
}