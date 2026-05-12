@echo off
REM 生成 design\详细设计文档_卫星测试数据预处理与数据分析平台_V2.0.docx
REM 依赖: Python 3、网络（Kroki）、pandoc
cd /d "%~dp0.."
python "%~dp0export_detail_design_docx.py"
if errorlevel 1 exit /b 1
echo Done.
