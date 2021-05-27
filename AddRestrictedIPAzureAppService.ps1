# How to run script?
# .\AddRestrictedIPAzureAppService.ps1 -ResourceGroupName "YourResourceGroup" -AppServiceName "YourAppServiceName" -SubscriptionId "YourSubscriptionGuid" 

# Sample example to run
# .\AddRestrictedIPAzureAppService.ps1 -ResourceGroupName "QnaMakerCoreTeam" -AppServiceName "QnAMakerDriTestCentralIndia" -SubscriptionId "a18d1e5d-191d-4523-85f3-fd72c8fe5d63" 

Param( 
    [Parameter(Mandatory = $true)] 
    [string] $ResourceGroupName, 
    [Parameter(Mandatory = $true)] 
    [string] $AppServiceName, 
    [Parameter(Mandatory = $true)] 
    [string] $SubscriptionId
)

$ErrorActionPreference = "Stop"

Import-Module Az

#If logged in, there's an azcontext, so we skip login
#if($Null -eq (Get-AzContext)){
    Login-AzAccount
#}

Select-AzSubscription -SubscriptionId $SubscriptionId

#grab the latest available api version
$APIVersion = ((Get-AzResourceProvider -ProviderNamespace Microsoft.Web).ResourceTypes | Where-Object ResourceTypeName -eq sites).ApiVersions[0]

$WebAppConfig = Get-AzResource -ResourceName $AppServiceName -ResourceType Microsoft.Web/sites/config -ResourceGroupName $ResourceGroupName -ApiVersion $APIVersion

# Allow only Service Tag 'CognitiveSerivcesManagement'
$WebAppConfig.Properties.ipSecurityRestrictions += @{
    ipAddress = "CognitiveServicesManagement"; 
    action = "Allow";
    priority = "100";
    name = "cognitive services Tag";
    description = "allow traffic";
    tag = "ServiceTag";
}

Set-AzResource -ResourceId $WebAppConfig.ResourceId -Properties $WebAppConfig.Properties -ApiVersion $APIVersion
