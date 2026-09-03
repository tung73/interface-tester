# Interface Tester

Universal **.NET Framework 4.8** mutual-TLS and SOAP connection tester.

It checks each configured HTTPS interface with its **own P12 client certificate**:

1. TCP connect
2. Mutual TLS handshake for TLS 1.0, 1.1, 1.2, and 1.3
3. Application probe (SOAP `testConnection` or SOAP `Test`)

All interface settings live in **`InterfaceTester\App.config`**. There is no `Interfaces.xml`.

## Endpoints

| # | Name | URL | Probe |
| --- | --- | --- | --- |
| 1 | `CAPS-WLS-UAT` | `https://uat.wls.caps.customs.hksarg:8102/rcaps_ws/CapsCommonInterfaceServiceForDCS` | TLS + SOAP `testConnection("", null)` |
| 2 | `DCS-CAPS-UAT` | `https://uat.int.dcs.customs.hksarg:8443/CAPS/WebServices.asmx` | TLS + SOAP `Test` |
| 3 | `DCS-OCR-DEV` | `https://dev.ext.dcs.customs.hksarg:8443/OCR/WebServices.asmx` | TLS + SOAP `Test` |

ASMX SOAP envelope (`soap/TestRequest.xml`):

```xml
<?xml version="1.0" encoding="utf-8"?>
<soap:Envelope xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
               xmlns:xsd="http://www.w3.org/2001/XMLSchema"
               xmlns:soap="http://schemas.xmlsoap.org/soap/envelope/">
  <soap:Body>
    <Test xmlns="http://tempuri.org/" />
  </soap:Body>
</soap:Envelope>
```

`SOAPAction` is `http://tempuri.org/Test`.

CAPS WLS SOAP (`soap/CapsTestConnection.xml`) matches the old program `ws.testConnection("", null)`:

```xml
<caps:testConnection xmlns:caps="http://endpoint.dcs.ws_i.caps/">
  <arg0></arg0>
</caps:testConnection>
```

`SOAPAction` is empty. The saved return value is the SOAP `<return>` string.

## Setup

1. Pull the latest `main`.
2. Close Visual Studio.
3. Double-click **`Reset-VS.bat`** (clears a cached “Skipped Build” setting and opens the solution).
   Or double-click **`InterfaceTester.sln`**. Do **not** use File → Open → Folder.
4. Right-click **InterfaceTester** → **Set as Startup Project** (the name becomes bold).
5. **Build → Rebuild Solution**. The output must say **1 succeeded**, not **1 skipped**.
6. Press **F5**.
7. The console asks which interface to test. Enter **1**, **2**, **3**, or **A** (all).

If Visual Studio still skips the project or F5 says the startup project cannot be launched, double-click **`Run.bat`**. That builds with MSBuild and runs the exe without the debugger.

This project is a **.NET Framework 4.8 Console Application**. Visual Studio needs:

- Workload: **.NET desktop development**
- Component: **.NET Framework 4.8 targeting pack**

Install them from Visual Studio Installer if the project loads as incompatible / unloaded.

Before a real connection test, copy each interface P12 into `InterfaceTester\certs\`:

- `caps-wls-uat.p12`
- `dcs-caps-uat.p12`
- `dcs-ocr-dev.p12`

Then set each `InterfaceN.P12Password` in `InterfaceTester\App.config`.

## Run

When you start the tester, it prints a menu:

```text
Which interface do you want to test?

  1. CAPS-WLS-UAT
  2. DCS-CAPS-UAT
  3. DCS-OCR-DEV
  A. Test all

Enter 1, 2, 3, or A:
```

You can also pass the choice on the command line:

```text
InterfaceTester.exe
InterfaceTester.exe 1
InterfaceTester.exe 2
InterfaceTester.exe 3
InterfaceTester.exe A
InterfaceTester.exe --list
```

If stdin is redirected (no keyboard), it tests all enabled interfaces.

## Add another interface

Add the next numbered block in `App.config`. Each interface needs its own P12.

```xml
<add key="Interface4.Name" value="MY-SERVICE" />
<add key="Interface4.Url" value="https://host:8443/path" />
<add key="Interface4.P12Path" value="certs\my-service.p12" />
<add key="Interface4.P12Password" value="secret" />
<add key="Interface4.Enabled" value="true" />
<add key="Interface4.Probes" value="tls,soap" />
<add key="Interface4.SoapAction" value="http://tempuri.org/Test" />
<add key="Interface4.SoapEnvelopePath" value="soap\TestRequest.xml" />
```

`Probes` can be `tls`, `http`, `wsdl`, `soap` (comma-separated).

If the service uses an internal CA that is not in the Windows trust store, set either:

- `InterfaceN.AcceptUntrustedServerCertificate` to `true` on that interface, or
- `AcceptUntrustedServerCertificates` to `true` in `App.config`

Server certificates are still printed (subject, issuer, chain status) so you can see why validation failed.

## Logs and API return values

Each run writes a timestamped folder under `InterfaceTester\bin\Debug\logs\` (or `LogDirectory` in `App.config`):

```text
logs\2026-09-02_223045\
  run.log
  summary.txt
  DCS-CAPS-UAT_SOAP_..._response.xml
  DCS-CAPS-UAT_SOAP_..._return_value.txt
```

- `run.log` is the full test transcript (same text as the console).
- `*_response.xml` is the raw HTTP/SOAP body.
- `*_return_value.txt` is the extracted SOAP `Test()` return value (`TestResult`).

The console also prints `API return value` after each SOAP/HTTP probe.

## Notes

- A SOAP fault or HTTP 4xx/5xx still means **the connection worked** (TCP + mTLS + HTTP). The summary marks that as `CONNECTED` rather than a TLS failure.
- P12/PFX files and generated logs are gitignored. Do not commit certificates or passwords.
