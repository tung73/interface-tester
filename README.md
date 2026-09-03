# Interface Tester

Universal **.NET Framework 4.8** mutual-TLS and SOAP connection tester.

It checks each configured HTTPS interface with its **own P12 client certificate**:

1. TCP connect
2. Mutual TLS handshake for TLS 1.0, 1.1, 1.2, and 1.3
3. Application probe (HTTP/WSDL, or SOAP `Test` pinned to TLS 1.2 and TLS 1.3)

All interface settings live in **`App.config`**. There is no `Interfaces.xml`.

This repo uses the Visual Studio project that already debugs with F5 (`InterfaceTester.sln` next to `InterfaceTester.csproj`).

## Endpoints

| # | Name | URL | Probe |
| --- | --- | --- | --- |
| 1 | `CAPS-WLS-UAT` | `https://uat.wls.caps.customs.hksarg:8102/rcaps_ws/CapsCommonInterfaceServiceForDCS` | TLS handshake + SOAP `testConnection` on TLS 1.2 and TLS 1.3 |
| 2 | `DCS-CAPS-UAT` | `https://uat.int.dcs.customs.hksarg:8443/CAPS/WebServices.asmx` | TLS handshake + SOAP `Test` on TLS 1.2 and TLS 1.3 |
| 3 | `DCS-OCR-DEV` | `https://dev.ext.dcs.customs.hksarg:8443/OCR/WebServices.asmx` | TLS handshake + SOAP `Test` on TLS 1.2 and TLS 1.3 |

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

When `Probes` includes `soap`, the tester does **not** use `HttpWebRequest` for SOAP. It opens a new TCP+mTLS connection twice and posts the envelope:

1. `SslStream` pinned to **TLS 1.2 only**, then SOAP POST
2. `SslStream` pinned to **TLS 1.3 only**, then SOAP POST

Each SOAP line in the summary shows the negotiated TLS version, so you can see 1.2 and 1.3 separately.

SOAP is sent **only** if `SslStream.SslProtocol` matches the pin. `SslProtocols.Tls12` is 3072 (`0xC00`); TLS 1.3 is 12288 (`0x3000`). A mismatch is a FAIL and the envelope is not posted. Handshake probes (`tls`) still test 1.0–1.3 separately.


## Setup

1. Pull the latest `main`.
2. Double-click **`InterfaceTester.sln`**. Do **not** use File → Open → Folder.
3. Right-click **InterfaceTester** → **Set as Startup Project** if it is not already bold.
4. Press **F5**.
5. The console asks which interface to test. Enter **1**, **2**, **3**, or **A** (all).

This project is a **.NET Framework 4.8 Console Application**. Visual Studio needs:

- Workload: **.NET desktop development**
- Component: **.NET Framework 4.8 targeting pack**

P12 path and password are set in `App.config` (`InterfaceN.P12Path` / `InterfaceN.P12Password`).

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
<add key="Interface4.P12Path" value="C:\path\to\cert.p12" />
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

## Logs and API return values

Each run writes a timestamped folder under `bin\Debug\logs\` (or `LogDirectory` in `App.config`):

```text
logs\2026-09-02_223045\
  run.log
  tls_proof.txt
  summary.txt
  DCS-CAPS-UAT_SOAP_..._response.xml
  DCS-CAPS-UAT_SOAP_..._return_value.txt
```

- `run.log` is the full test transcript (same text as the console).
- `tls_proof.txt` is the SOAP TLS pin, negotiated version, numeric `SslProtocols` value, cipher, and whether SOAP was sent.
- `*_response.xml` is the raw HTTP/SOAP body.
- `*_return_value.txt` is the extracted SOAP return value.

`run.log` and `tls_proof.txt` are the TLS-version proof:

```
    TLS pin: TLS 1.2
    Negotiated TLS: TLS 1.2 (SslProtocols=3072 / 0xC00)
```

## Notes

- A SOAP fault or HTTP 4xx/5xx still means **the connection worked** (TCP + mTLS + HTTP). The summary marks that as `CONNECTED` rather than a TLS failure.
- P12/PFX files and generated logs are gitignored. Do not commit certificates or passwords.
