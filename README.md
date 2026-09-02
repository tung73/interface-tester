# Interface Tester

Universal **.NET Framework 4.8** mutual-TLS and SOAP connection tester.

It checks each configured HTTPS interface with its **own P12 client certificate**:

1. TCP connect
2. Mutual TLS handshake for TLS 1.0, 1.1, 1.2, and 1.3
3. Application probe (HTTP GET, WSDL, or SOAP `Test`)

## Endpoints

| Name | URL | Probe |
| --- | --- | --- |
| `CAPS-WLS-UAT` | `https://uat.wls.caps.customs.hksarg:8102/rcaps_ws/CapsCommonInterfaceServiceForDCS` | TLS + HTTP GET + WSDL |
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

## Setup

1. Pull the latest `main`.
2. Double-click **`InterfaceTester.sln`** to open it in Visual Studio.
   Do **not** use File → Open → Folder.
3. If Solution Explorer says Folder View, click the solution-switch icon and choose `InterfaceTester.sln`.
4. Right-click **InterfaceTester** → **Set as Startup Project**.
5. Toolbar should be **Debug** and **Any CPU** (or x86 / x64; all of them build this console app).
6. Press F5.

If F5 still fails, double-click **`Run.bat`** in the repo root. That builds and runs without the debugger.

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

## Notes

- A SOAP fault or HTTP 4xx/5xx still means **the connection worked** (TCP + mTLS + HTTP). The summary marks that as `CONNECTED` rather than a TLS failure.
- P12/PFX files are gitignored. Do not commit certificates or passwords.
