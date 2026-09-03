using System;
using System.Xml;

namespace InterfaceTester
{
    internal static class SoapResponseParser
    {
        public static string ExtractReturnValue(string responseBody)
        {
            if (String.IsNullOrWhiteSpace(responseBody))
            {
                return "";
            }

            try
            {
                XmlDocument document = new XmlDocument();
                document.XmlResolver = null;
                document.LoadXml(responseBody);

                string[] localNames = new[]
                {
                    "TestResult",
                    "testResult",
                    "return",
                    "Return",
                    "Result"
                };

                for (int i = 0; i < localNames.Length; i++)
                {
                    XmlNode node = FindByLocalName(document.DocumentElement, localNames[i]);

                    if (node != null && !String.IsNullOrWhiteSpace(node.InnerText))
                    {
                        return node.InnerText.Trim();
                    }
                }

                string[] wrappers = new[]
                {
                    "testConnectionResponse",
                    "TestResponse"
                };

                for (int i = 0; i < wrappers.Length; i++)
                {
                    XmlNode response = FindByLocalName(
                        document.DocumentElement,
                        wrappers[i]);

                    if (response != null && !String.IsNullOrWhiteSpace(response.InnerText))
                    {
                        return response.InnerText.Trim();
                    }
                }
            }
            catch (XmlException)
            {
            }

            return "";
        }

        private static XmlNode FindByLocalName(XmlNode node, string localName)
        {
            if (node == null)
            {
                return null;
            }

            if (String.Equals(node.LocalName, localName, StringComparison.Ordinal))
            {
                return node;
            }

            foreach (XmlNode child in node.ChildNodes)
            {
                XmlNode match = FindByLocalName(child, localName);

                if (match != null)
                {
                    return match;
                }
            }

            return null;
        }
    }
}
