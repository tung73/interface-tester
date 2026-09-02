Place one P12 client certificate per interface in this folder.

Default filenames expected by Interfaces.xml:

  caps-wls-uat.p12     CAPS-WLS-UAT   (uat.wls.caps.customs.hksarg:8102)
  dcs-caps-uat.p12     DCS-CAPS-UAT   (uat.int.dcs.customs.hksarg:8443)
  dcs-ocr-dev.p12      DCS-OCR-DEV    (dev.ext.dcs.customs.hksarg:8443)

Then set the matching p12Password in Interfaces.xml.

P12 / PFX files are gitignored. Do not commit certificates or passwords.
