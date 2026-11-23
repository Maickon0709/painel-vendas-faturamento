@echo off
rem Recompila o PainelPedidos.exe com o compilador C# que JA VEM no Windows.
cd /d "%~dp0"
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
"%CSC%" /nologo /target:winexe /optimize+ /win32icon:icone.ico /resource:painel_modelo.html,PainelPedidos.painel_modelo.html /r:System.Windows.Forms.dll /r:System.Drawing.dll /out:..\PainelPedidos.exe PdfTexto.cs Resumo.cs Licitacoes.cs Exportador.cs Dialogos.cs TelaPainel.cs Janela.cs
if errorlevel 1 (echo. & echo Deu erro na compilacao.) else (echo. & echo Pronto! PainelPedidos.exe atualizado na pasta de cima.)
pause
