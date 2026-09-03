# Interface Tester

Universal **.NET Framework 4.8** mutual-TLS and SOAP connection tester.

It checks each configured HTTPS interface with its **own P12 client certificate**:

1. TCP connect
2. Mutual TLS handshake for TLS 1.0, 1.1, 1.2, and 1.3
3. Application probe (SOAP `testConnection` or SOAP `Test`)

## Endpoints

| Name | URL | Probe |
| --- | --- | --- |
| `CAPS-WLS-UAT` | `https://uat.wls.caps.customs.hksarg:8102/rcaps_ws/CapsCommonInterfaceServiceForDCS` | TLS + SOAP `testConnection("", null)` |
| `DCS-CAPS-UAT` | `https://uat.int.dcs.customs.hksarg:8443/CAPS/WebServices.asmx` | TLS + SOAP `Test` |
| `DCS-OCR-DEV` | `https://dev.ext.dcs.customs.hksarg:8443/OCR/WebServices.asmx` | TLS + SOAP `Test` |

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

If Visual Studio still skips the project or F5 says the startup project cannot be launched, double-click **`Run.bat`**. That builds with MSBuild and runs the exe without the debugger.

This project is a **.NET Framework 4.8 Console Application**. Visual Studio needs:

- Workload: **.NET desktop development**
- Component: **.NET Framework 4.8 targeting pack**

Install them from Visual Studio Installer if the project loads as incompatible / unloaded.

Before a real connection test, copy each interface P12 into `InterfaceTester\certs\`:

- `caps-wls-uat.p12`
- `dcs-caps-uat.p12`
- `dcs-ocr-dev.p12`

Then set each `p12Password` in `InterfaceTester\Interfaces.xml`.

## Run

```text
InterfaceTester.exe
InterfaceTester.exe --list
InterfaceTester.exe DCS-CAPS-UAT
InterfaceTester.exe CAPS-WLS-UAT DCS-OCR-DEV
```

## Add another interface

Add an `<interface>` row to `Interfaces.xml`. Each row needs its own `p12Path`.

```xml
<interface
    name="MY-SERVICE"
    url="https://host:8443/path"
    p12Path="certs\my-service.p12"
    p12Password="secret"
    probes="tls,soap"
    soapAction="http://tempuri.org/Test"
    soapEnvelopePath="soap\TestRequest.xml" />
```

`probes` can be `tls`, `http`, `wsdl`, `soap` (comma-separated).

If the service uses an internal CA that is not in the Windows trust store, set either:

- `acceptUntrustedServerCertificate="true"` on that interface, or
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
