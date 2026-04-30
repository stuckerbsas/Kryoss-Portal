# PSScriptAnalyzer Settings for TeamLogic IT / Kryoss MSP Scripts
# Place this file in the project root. Run: Invoke-ScriptAnalyzer -Path . -Settings .\PSScriptAnalyzerSettings.psd1 -Recurse
#
# Reference: https://learn.microsoft.com/en-us/powershell/utility-modules/psscriptanalyzer/rules-recommendations

@{
    # ── Severity ───────────────────────────────────────────────
    Severity = @('Error', 'Warning', 'Information')

    # ── Rules to Include ───────────────────────────────────────
    IncludeRules = @(
        # Cmdlet Design
        'PSUseApprovedVerbs'
        'PSReservedCmdletChar'
        'PSReservedParams'
        'PSUseShouldProcessForStateChangingFunctions'
        'PSUseSupportsShouldProcess'
        'PSShouldProcess'
        'PSUseSingularNouns'
        'PSAvoidDefaultValueSwitchParameter'
        'PSMissingModuleManifestField'

        # Script Functions
        'PSAvoidUsingCmdletAliases'
        'PSAvoidUsingWMICmdlet'
        'PSAvoidUsingEmptyCatchBlock'
        'PSUseCmdletCorrectly'
        'PSAvoidUsingPositionalParameters'
        'PSAvoidGlobalVars'
        'PSUseDeclaredVarsMoreThanAssignments'
        'PSAvoidUsingInvokeExpression'

        # Scripting Style
        'PSAvoidUsingWriteHost'
        'PSProvideCommentHelp'

        # Script Security
        'PSAvoidUsingPlainTextForPassword'
        'PSUsePSCredentialType'
        'PSAvoidUsingComputerNameHardcoded'
        'PSAvoidUsingConvertToSecureStringWithPlainText'
        'PSAvoidUsingUsernameAndPasswordParams'

        # Code Quality
        'PSUseConsistentWhitespace'
        'PSUseConsistentIndentation'
        'PSPlaceOpenBrace'
        'PSPlaceCloseBrace'
        'PSAlignAssignmentStatement'
        'PSUseCorrectCasing'
        'PSAvoidUsingDoubleQuotesForConstantString'
        'PSAvoidLongLines'
        'PSAvoidSemicolonsAsLineTerminators'
        'PSAvoidTrailingWhitespace'

        # Compatibility
        'PSUseCompatibleSyntax'
        'PSUseCompatibleCmdlets'
    )

    # ── Rules to Exclude ───────────────────────────────────────
    # PSAvoidUsingWriteHost: We intentionally use Write-Host for NinjaRMM console output.
    # We include the rule but suppress it selectively in code with [Diagnostics.CodeAnalysis.SuppressMessageAttribute()]
    ExcludeRules = @()

    # ── Rule Configuration ─────────────────────────────────────
    Rules = @{

        # ── Indentation: 4 spaces, no tabs ────────────────────
        PSUseConsistentIndentation = @{
            Enable          = $true
            IndentationSize = 4
            PipelineIndentation = 'IncreaseIndentationForFirstPipeline'
            Kind            = 'space'
        }

        # ── Braces: OTBS (One True Brace Style) ──────────────
        PSPlaceOpenBrace = @{
            Enable             = $true
            OnSameLine         = $true
            NewLineAfter       = $true
            IgnoreOneLineBlock = $true
        }

        PSPlaceCloseBrace = @{
            Enable             = $true
            NewLineAfter       = $false
            IgnoreOneLineBlock = $true
            NoEmptyLineBefore  = $false
        }

        # ── Whitespace ────────────────────────────────────────
        PSUseConsistentWhitespace = @{
            Enable                                  = $true
            CheckInnerBrace                         = $true
            CheckOpenBrace                          = $true
            CheckOpenParen                          = $true
            CheckOperator                           = $true
            CheckPipe                               = $true
            CheckPipeForRedundantWhitespace         = $false
            CheckSeparator                          = $true
            CheckParameter                          = $false
            IgnoreAssignmentOperatorInsideHashTable  = $true
        }

        # ── Assignment alignment ──────────────────────────────
        PSAlignAssignmentStatement = @{
            Enable         = $true
            CheckHashtable = $true
        }

        # ── Line length: 115 chars max ────────────────────────
        PSAvoidLongLines = @{
            Enable            = $true
            MaximumLineLength = 115
        }

        # ── Semicolons: avoid as line terminators ─────────────
        PSAvoidSemicolonsAsLineTerminators = @{
            Enable = $true
        }

        # ── Constant strings: prefer single quotes ───────────
        PSAvoidUsingDoubleQuotesForConstantString = @{
            Enable = $true
        }

        # ── Comment-based help: require all sections ──────────
        PSProvideCommentHelp = @{
            Enable                  = $true
            ExportedOnly            = $false
            BlockComment            = $true
            VSCodeSnippetCorrection = $false
            Placement               = 'before'
        }

        # ── Compatibility: target PowerShell 5.1+ ─────────────
        PSUseCompatibleSyntax = @{
            Enable         = $true
            TargetVersions = @('5.1', '7.0')
        }

        # ── Aliases: enforce full cmdlet names ────────────────
        PSAvoidUsingCmdletAliases = @{
            allowlist = @()
        }
    }
}
