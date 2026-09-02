Each test run writes a timestamped folder here, for example:

  2026-09-02_223045\
    run.log
    summary.txt
    DCS-CAPS-UAT_SOAP_http___tempuri.org_Test_response.xml
    DCS-CAPS-UAT_SOAP_http___tempuri.org_Test_return_value.txt

run.log is the full console transcript.
*_response.xml is the raw HTTP/SOAP body.
*_return_value.txt is the extracted Test() API return value (TestResult).

These files are created at runtime and are gitignored.
