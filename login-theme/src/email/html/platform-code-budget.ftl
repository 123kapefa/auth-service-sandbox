<#-- Расход на коды — админам проекта, из плагина code-login (BudgetAlerts): атрибуты budgetTitle, budgetText,
     budgetEffect и budgetNote. -->
<#import "template.ftl" as layout>
<@layout.emailLayout>
<@layout.title budgetTitle/>
<@layout.paragraph budgetText/>
<@layout.paragraph budgetEffect/>
<@layout.note budgetNote/>
</@layout.emailLayout>
