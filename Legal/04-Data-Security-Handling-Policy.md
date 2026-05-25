# DATA SECURITY & HANDLING POLICY

**QuickBooks Time-Warp Software**

**Effective Date:** May 24, 2026  
**Last Updated:** May 24, 2026

---

## 🔒 ZERO-UPLOAD SECURITY ARCHITECTURE

QuickBooks Time-Warp® employs a **100% local processing** architecture. This is not merely a feature — it is a fundamental design principle that eliminates entire categories of security risk:

| Threat | Traditional Services | QuickBooks Time-Warp® |
|--------|---------------------|----------------------|
| Data in transit | ❌ File uploaded over internet | ✅ **No transmission — data stays local** |
| Third-party access | ❌ Vendor handles your file | ✅ **No third-party access** |
| Data at rest (vendor) | ❌ Stored on vendor servers | ✅ **Zero vendor storage** |
| Breach exposure | ❌ Vendor breach = your data exposed | ✅ **No vendor data = no breach risk** |
| Compliance burden | ❌ Must verify vendor compliance | ✅ **Data never leaves your control** |

**Your QuickBooks company file never leaves your computer. Period.**

---

## Executive Summary

This Data Security & Handling Policy outlines the comprehensive security measures, data handling procedures, and best practices implemented by Our System Administrator (oursystemadmin.com) and Live Remote Support, Inc (liveremotesupport.net) for QuickBooks Time-Warp software. This policy ensures the confidentiality, integrity, and availability of sensitive financial data processed by the Software.

---

## 1. Policy Scope and Application

### 1.1 Applicability
This policy applies to:
- QuickBooks Time-Warp software and all its components
- All data processed by the Software
- All users, administrators, and support personnel
- Development, testing, and production environments
- All systems and networks where the Software operates

### 1.2 Purpose
This policy establishes:
- Security standards and controls
- Data handling procedures
- User responsibilities
- Incident response protocols
- Compliance requirements
- Audit and monitoring procedures

### 1.3 Policy Updates
This policy is reviewed annually and updated as needed to address:
- Emerging security threats
- Changes in technology
- Regulatory requirements
- Industry best practices
- Lessons learned from incidents

---

## 2. Fundamental Security Principle: Local Processing

### 2.1 Core Architecture
**QuickBooks Time-Warp operates on a "zero-transmission" architecture:**

- **100% Local Processing:** All data operations occur entirely on the user's local computer
- **No Cloud Storage:** The Software does not upload, transmit, or store data in the cloud
- **No Internet Transmission:** QuickBooks financial data never leaves your system via the Software
- **No External Servers:** The Software does not communicate with external data storage servers
- **No Third-Party Access:** No third parties can access your data through the Software

### 2.2 Network Activity
The Software's ONLY network activities are:

(a) **License Validation:** Encrypted communication to verify software license (no financial data transmitted)  
(b) **Update Checks:** Checking for software updates (no financial data transmitted)  
(c) **Support Sessions:** Only with explicit user authorization for remote support

**Critical:** Financial data from QuickBooks files is NEVER transmitted over any network.

### 2.3 Air-Gap Compatible
The Software can operate in air-gapped environments (systems with no internet connectivity) after initial license activation.

---

## 3. Data Classification

### 3.1 Classification Levels

**CRITICAL - Level 1: Financial Transaction Data**
- Customer and vendor financial records
- Bank account information
- Payment and invoice data
- General ledger transactions
- Sensitive financial computations

**HIGH - Level 2: Business Information**
- Company configuration settings
- Chart of accounts structure
- Custom field definitions
- Report templates
- Business processes

**MEDIUM - Level 3: Operational Data**
- Migration logs (anonymized)
- Performance metrics
- Error messages (sanitized)
- System configuration

**LOW - Level 4: Public Information**
- Software version numbers
- General documentation
- Public website content

### 3.2 Handling Requirements
Each classification level has specific handling requirements detailed in Section 5.

---

## 4. Encryption Standards

### 4.1 Data at Rest

**QuickBooks Company Files:**
- Original files: Protected by QuickBooks' native encryption
- Working files: AES-256 encryption during processing
- Temporary files: AES-256 encryption while in use
- Backup files: Inherit source file encryption

**Configuration Data:**
- License keys: AES-256 encryption
- User preferences: AES-128 encryption
- Application settings: AES-128 encryption

### 4.2 Data in Memory

**Memory Protection:**
- Sensitive data structures encrypted in RAM when possible
- Memory pages marked as non-swappable for critical operations
- Secure memory allocation for cryptographic operations
- Memory scrubbing after sensitive operations

**Buffer Security:**
- Secure buffers for password and key entry
- Automatic zeroing of buffers after use
- Protection against memory dump attacks
- Prevention of sensitive data in crash dumps

### 4.3 Encryption Algorithms

**Approved Algorithms:**
- **Symmetric:** AES-256, AES-128
- **Asymmetric:** RSA-2048, RSA-4096
- **Hashing:** SHA-256, SHA-512
- **Key Derivation:** PBKDF2, bcrypt

**Prohibited Algorithms:**
- DES, 3DES (deprecated)
- MD5, SHA-1 (cryptographically broken)
- RC4 (insecure)
- Any algorithm with known vulnerabilities

### 4.4 Key Management

**Key Generation:**
- Cryptographically secure random number generators (CSRNG)
- Minimum key lengths as specified above
- Regular key rotation for long-term keys

**Key Storage:**
- Operating system secure key storage (Windows DPAPI, macOS Keychain, Linux Secret Service)
- Never stored in plain text
- Protected against unauthorized access

**Key Destruction:**
- Secure deletion when no longer needed
- Multiple-pass overwriting
- Verification of destruction

---

## 5. Secure File Handling

### 5.1 Source File Protection

**Read-Only Access:**
- Source QuickBooks files are opened in read-only mode
- Original files are never modified directly
- File locks prevent concurrent access
- Integrity verification before and after reading

**Integrity Verification:**
- SHA-256 checksums calculated for source files
- Verification that files are not corrupted
- Detection of unexpected modifications
- Validation against QuickBooks file format specifications

### 5.2 Working File Management

**Creation:**
- Working files created in secure temporary directories
- Permissions set to restrict access to current user only
- Encrypted immediately upon creation
- Unique identifiers to prevent collisions

**Processing:**
- All operations performed on encrypted working copies
- Original data preserved throughout process
- Transaction logging for audit trails
- Rollback capability in case of errors

**Isolation:**
- Working files segregated from other applications
- Dedicated temporary directories with restricted access
- Prevention of cross-contamination between migrations
- Sandboxing where supported by operating system

### 5.3 Output File Security

**Destination Files:**
- Created with user-specified permissions
- Inherit or exceed security of source files
- Integrity verification after creation
- Comparison against expected results

**Validation:**
- Data integrity checks
- Format validation
- Completeness verification
- Reconciliation against source data

### 5.4 Automatic Cleanup

**Post-Migration Cleanup:**
- All working files securely deleted after successful migration
- Multi-pass overwriting (DoD 5220.22-M standard)
- Verification of deletion
- Cleanup of memory structures

**Error Condition Cleanup:**
- Cleanup triggered even if migration fails
- Partial results securely deleted
- Error logs sanitized of sensitive data
- User notification of cleanup status

**Manual Cleanup:**
- User-accessible cleanup function
- Verification and reporting of cleanup actions
- Ability to verify no sensitive data remains

---

## 6. Access Controls

### 6.1 User Authentication

**Local Authentication:**
- Relies on operating system authentication
- No separate user accounts within Software
- Respects OS-level user permissions
- Integration with enterprise authentication where available

**License Verification:**
- Encrypted communication with license server
- Certificate-based validation
- No financial data transmitted during verification
- Offline grace periods for temporary connectivity loss

### 6.2 File System Permissions

**Minimum Privilege:**
- Software requests only necessary file system permissions
- Read access to source files
- Write access only to user-specified output locations
- Temporary directory access for working files

**Permission Verification:**
- Pre-flight checks before operations
- Verification that user has necessary permissions
- Clear error messages if permissions insufficient
- Guidance on resolving permission issues

### 6.3 Administrative Controls

**Installation:**
- Administrator rights required for installation
- Option for per-user vs. system-wide installation
- Security settings configured during installation

**Configuration:**
- Security settings protected from unauthorized modification
- Administrative privileges required for security policy changes
- Audit logging of configuration changes

---

## 7. Audit Trail Capabilities

### 7.1 Operation Logging

**What is Logged:**
- Start and completion times of migrations
- Source and destination file paths (not contents)
- Success or failure status
- Error messages (sanitized of financial data)
- User who initiated operation
- Software version used

**What is NOT Logged:**
- Financial transaction details
- Customer or vendor names
- Dollar amounts
- Account numbers
- Any personally identifiable information (PII)

### 7.2 Log Security

**Protection:**
- Log files encrypted at rest
- Access restricted to authorized users
- Integrity protection (tamper detection)
- Secure timestamps

**Retention:**
- Configurable retention periods
- Automatic archival of old logs
- Secure deletion after retention period
- Compliance with regulatory requirements

### 7.3 Log Review

**Monitoring:**
- Regular review for security events
- Anomaly detection
- Identification of unauthorized access attempts
- Performance monitoring

**Accessibility:**
- User-friendly log viewers
- Export capabilities for external analysis
- Integration with SIEM systems (enterprise edition)

---

## 8. Security Best Practices

### 8.1 Pre-Migration Security Checklist

Before performing any migration, users should:

1. **Backup Verification**
   - Create complete backup of QuickBooks company file
   - Verify backup is complete and restorable
   - Store backup in secure location separate from original
   - Document backup creation date and time

2. **System Security**
   - Ensure antivirus software is up-to-date and active
   - Verify operating system security patches are current
   - Close unnecessary applications
   - Disable remote access during migration (if applicable)

3. **Environment Preparation**
   - Ensure adequate disk space for working files
   - Verify sufficient memory for operation
   - Close QuickBooks and all related applications
   - Disable scheduled backups during migration

4. **Access Control**
   - Ensure only authorized personnel are present
   - Lock workstation from physical access if needed
   - Verify network security if in shared environment

### 8.2 During Migration Security

**Monitoring:**
- Do not leave computer unattended during migration
- Monitor for unusual system behavior
- Watch for unexpected error messages
- Verify adequate progress is being made

**Incident Response:**
- If suspicious activity detected, stop migration immediately
- Document any anomalies
- Contact support if security concerns arise
- Preserve evidence if security incident suspected

### 8.3 Post-Migration Security

**Verification:**
- Review migration logs for anomalies
- Verify working files were cleaned up
- Confirm destination file integrity
- Reconcile migrated data against source

**Validation:**
- Open migrated file in QuickBooks
- Verify data accuracy with sample transactions
- Run QuickBooks verify data utility
- Consult with accountant if any discrepancies

**Documentation:**
- Document migration completion
- Record verification results
- Retain migration logs as appropriate
- Update backup schedule

---

## 9. Secure Development Practices

### 9.1 Development Lifecycle Security

**Secure Design:**
- Threat modeling during design phase
- Security requirements defined upfront
- Privacy-by-design principles
- Secure architecture patterns

**Secure Coding:**
- Following OWASP secure coding guidelines
- Input validation and sanitization
- Output encoding
- Prevention of injection attacks
- Buffer overflow protection
- Integer overflow checks

**Code Review:**
- Mandatory peer review of all code
- Security-focused code reviews
- Automated static analysis
- Manual security audits for critical components

### 9.2 Testing and Quality Assurance

**Security Testing:**
- Unit tests for security functions
- Integration tests for security boundaries
- Penetration testing by independent security firms
- Fuzzing and vulnerability scanning

**Test Data Security:**
- No use of real customer data in testing
- Synthetic test data generated for development
- Secure disposal of test data
- Isolated test environments

### 9.3 Vulnerability Management

**Identification:**
- Automated vulnerability scanning
- Security researcher bug bounty program
- Monitoring security advisories
- Internal security audits

**Response:**
- Risk-based prioritization
- Rapid patching of critical vulnerabilities
- Coordinated disclosure with security researchers
- Transparent communication with users

**Patch Management:**
- Regular security updates
- Emergency patches for critical issues
- Clear communication of patch contents
- Automatic update mechanisms (with user control)

---

## 10. Third-Party Components

### 10.1 Component Evaluation

**Selection Criteria:**
- Reputation and track record of vendor
- Security history and vulnerability response
- Active maintenance and support
- License compatibility
- Security certifications

**Due Diligence:**
- Security assessment before adoption
- Review of known vulnerabilities
- Evaluation of update frequency
- Assessment of community support

### 10.2 Component Management

**Inventory:**
- Comprehensive bill of materials (BOM)
- Version tracking
- License compliance
- Dependency mapping

**Updates:**
- Regular monitoring for updates
- Timely application of security patches
- Testing before deployment
- Rollback procedures

### 10.3 Supply Chain Security

**Vendor Management:**
- Vendor security assessments
- Contractual security requirements
- Right to audit clauses
- Incident notification requirements

**Verification:**
- Cryptographic verification of downloads
- Signature verification
- Checksum validation
- Secure distribution channels

---

## 11. Incident Response

### 11.1 Incident Definition

A security incident is any event that:
- Compromises confidentiality, integrity, or availability of data
- Violates security policy
- Represents unauthorized access or attempted access
- Indicates malware or malicious activity
- Involves data breach or suspected breach

### 11.2 Response Procedures

**Detection:**
- Monitoring systems and logs
- User reports
- Automated alerts
- Security tool notifications

**Analysis:**
- Initial assessment of scope and severity
- Collection of evidence
- Determination of root cause
- Impact assessment

**Containment:**
- Immediate actions to limit damage
- Isolation of affected systems
- Preservation of evidence
- Prevention of further compromise

**Eradication:**
- Removal of threat
- Closing of vulnerabilities
- System restoration
- Verification of security

**Recovery:**
- Restoration of normal operations
- Data recovery if needed
- System verification
- Enhanced monitoring

**Lessons Learned:**
- Post-incident review
- Documentation of incident
- Process improvement
- User communication

### 11.3 Notification Procedures

**Internal Notification:**
- Immediate notification to security team
- Escalation to management as appropriate
- Coordination across teams

**User Notification:**
- Notification within 72 hours of confirmed breach
- Clear explanation of incident
- Guidance on protective measures
- Contact information for questions

**Regulatory Notification:**
- Compliance with breach notification laws
- Timely reporting to authorities
- Documentation of notification
- Ongoing communication

### 11.4 Communication

**Transparency:**
- Honest communication about incidents
- Regular updates during investigation
- Full disclosure of findings
- Commitment to improvement

**Support:**
- Dedicated incident response contact
- Available resources for affected users
- Guidance on remediation steps

---

## 12. Business Continuity and Disaster Recovery

### 12.1 Data Backup

**User Responsibility:**
- Users must maintain their own backups of QuickBooks files
- Software does not provide backup services
- Guidance provided on backup best practices

**Software Backup:**
- Licensor maintains backups of Software source code
- Secure storage of development assets
- Geographic redundancy
- Regular backup testing

### 12.2 Disaster Recovery

**Recovery Time Objectives (RTO):**
- License server: 4 hours
- Support systems: 8 hours
- Website and documentation: 24 hours
- Software downloads: 4 hours

**Recovery Point Objectives (RPO):**
- License data: 1 hour
- Support tickets: 4 hours
- User account data: 1 hour

**Testing:**
- Annual disaster recovery drills
- Documentation of procedures
- Regular updates to DR plans

---

## 13. Compliance and Regulatory Considerations

### 13.1 Applicable Regulations

The Software is designed to support compliance with:

**Data Protection:**
- GDPR (General Data Protection Regulation)
- CCPA (California Consumer Privacy Act)
- State privacy laws
- International data protection laws

**Financial Regulations:**
- SOX (Sarbanes-Oxley Act) - for publicly traded companies
- GLBA (Gramm-Leach-Bliley Act) - for financial institutions
- Industry-specific regulations

**Security Standards:**
- NIST Cybersecurity Framework
- ISO 27001 information security standards
- CIS Controls

### 13.2 Compliance Support

**Documentation:**
- Comprehensive security documentation
- Audit trail capabilities
- Evidence collection features
- Compliance reporting

**Controls:**
- Technical controls aligned with frameworks
- Administrative controls and policies
- Physical security considerations
- Monitoring and logging

### 13.3 User Compliance Responsibility

**User Obligations:**
- Understanding applicable regulations
- Implementing appropriate controls
- Conducting own compliance assessments
- Maintaining documentation
- Consulting with compliance professionals

The Software is a tool; ultimate compliance responsibility rests with the user organization.

---

## 14. Physical Security

### 14.1 User Workstation Security

**Recommendations:**
- Secure physical location for workstations
- Locked server rooms for servers
- Access controls to work areas
- Screen privacy filters for sensitive areas
- Physical security when migrating sensitive data

### 14.2 Licensor Facilities

**Our Facilities:**
- Controlled access to offices
- Video surveillance
- Visitor management
- Secure storage of physical media
- Clean desk policies
- Secure disposal of documents

---

## 15. Personnel Security

### 15.1 Background Checks

All employees with potential access to customer systems undergo:
- Criminal background checks
- Employment verification
- Reference checks
- Education verification (for technical roles)

### 15.2 Training

**Security Training:**
- Initial security training for all personnel
- Annual refresher training
- Role-specific security training
- Phishing and social engineering awareness
- Incident response training

**Privacy Training:**
- Data privacy principles
- Regulatory requirements
- Customer confidentiality
- Proper data handling

### 15.3 Access Controls

**Least Privilege:**
- Access based on job requirements
- Regular review of access rights
- Immediate revocation upon termination
- Segregation of duties

**Monitoring:**
- Activity logging for privileged accounts
- Regular review of access logs
- Anomaly detection
- Insider threat monitoring

---

## 16. Monitoring and Continuous Improvement

### 16.1 Security Monitoring

**Continuous Monitoring:**
- Automated security scanning
- Log analysis and correlation
- Threat intelligence integration
- Anomaly detection

**Metrics:**
- Security incidents tracked
- Time to detect and respond
- Vulnerability remediation times
- User security awareness

### 16.2 Regular Assessments

**Internal Assessments:**
- Quarterly security reviews
- Annual comprehensive assessments
- Vulnerability scanning
- Configuration audits

**External Assessments:**
- Annual penetration testing
- Independent security audits
- Compliance assessments
- Third-party risk assessments

### 16.3 Continuous Improvement

**Process:**
- Regular review of security policies
- Incorporation of lessons learned
- Adoption of new security technologies
- Response to evolving threats
- Industry best practice adoption

---

## 17. User Security Responsibilities

### 17.1 Essential User Responsibilities

Users are responsible for:

1. **System Security**
   - Maintaining up-to-date antivirus and anti-malware
   - Installing operating system security updates
   - Using firewalls and security software
   - Securing network connections

2. **Data Protection**
   - Creating and maintaining backups
   - Protecting access to QuickBooks files
   - Securing workstations
   - Controlling physical access

3. **Access Control**
   - Using strong passwords
   - Protecting license keys
   - Limiting Software access to authorized users
   - Logging out when leaving workstation

4. **Incident Reporting**
   - Reporting suspected security incidents
   - Documenting anomalies
   - Contacting support for concerns
   - Preserving evidence

### 17.2 Security Configuration

**Recommended Settings:**
- Enable all Software security features
- Use strongest available encryption
- Enable audit logging
- Configure automatic cleanup
- Review security settings regularly

---

## 18. Contact Information

### 18.1 Security Concerns

For security-related questions or to report security incidents:

**Security Team**  
Email: security@oursystemadmin.com  
Email: security@liveremotesupport.net  
Phone: [Contact for urgent security matters]

**Response Time:**
- Critical security issues: Immediate response
- High-priority security issues: 4 hours
- Normal security inquiries: 24 hours

### 18.2 Vulnerability Reporting

**Responsible Disclosure:**
We welcome responsible disclosure of security vulnerabilities:

Email: security@oursystemadmin.com  
PGP Key: [Available on website]

**Our Commitment:**
- Acknowledge receipt within 48 hours
- Provide status updates
- Credit researchers (with permission)
- Work toward resolution
- Coordinate disclosure timing

### 18.3 General Support

**Our System Administrator**  
Website: https://oursystemadmin.com  
Email: support@oursystemadmin.com

**Live Remote Support, Inc**  
Website: https://liveremotesupport.net  
Email: support@liveremotesupport.net

---

## 19. Policy Compliance

### 19.1 Acknowledgment

By using QuickBooks Time-Warp software, users acknowledge:
- Understanding of this Security Policy
- Agreement to comply with security requirements
- Responsibility for their own security practices
- Obligation to report security concerns

### 19.2 Violations

Violations of this policy may result in:
- License termination
- Legal action for damages
- Reporting to authorities (for illegal activity)
- Denial of future services

### 19.3 Policy Review

This policy is reviewed:
- Annually at minimum
- After significant security incidents
- When regulations change
- As new threats emerge

---

## Legal Notices

### Copyright
© 2026 Our System Administrator and Live Remote Support, Inc. All rights reserved.

### Trademarks
- QuickBooks Time-Warp is a trademark of Our System Administrator and Live Remote Support, Inc.
- QuickBooks is a registered trademark of Intuit Inc.
- All other trademarks are property of their respective owners.

### Disclaimers
This policy describes our security practices but does not constitute a warranty. Security is a shared responsibility between Licensor and users. No security system is impenetrable, and we cannot guarantee absolute security.

---

**Document Version:** 1.0  
**Effective Date:** May 24, 2026  
**Last Review Date:** May 24, 2026  
**Next Scheduled Review:** May 24, 2027  
**Policy Owner:** Chief Information Security Officer  
**Approved by:** Executive Management
