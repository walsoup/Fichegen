from unittest.mock import patch

from core.workers import ExportWorker


def test_export_worker_pdf_evaluation_success():
    captured = {}

    worker = ExportWorker(
        export_format="pdf",
        content_type="evaluation",
        markdown="# Eval",
        class_level="cm2",
        output_dir="fiches",
        template_name="Normal",
        subject="Sciences",
        lesson_topic="Lecon",
        topics_list=["Topic A"],
        show_meta_banner=True,
    )

    worker.success.connect(lambda path, fmt, ctype: captured.update({"path": path, "fmt": fmt, "ctype": ctype}))

    with patch("document.pdf.save_evaluation_to_pdf", return_value="/tmp/eval.pdf") as mock_save:
        worker.run()

    mock_save.assert_called_once()
    assert captured["path"] == "/tmp/eval.pdf"
    assert captured["fmt"] == "pdf"
    assert captured["ctype"] == "evaluation"


def test_export_worker_unsupported_format_fails():
    captured = {}

    worker = ExportWorker(
        export_format="txt",
        content_type="fiche",
        markdown="# Fiche",
        class_level="cm1",
        output_dir="fiches",
        template_name="Normal",
        subject="Français",
        lesson_topic="Topic",
        topics_list=["Topic"],
        show_meta_banner=False,
    )

    worker.failed.connect(lambda message: captured.update({"message": message}))
    worker.run()

    assert "Unsupported export format" in captured["message"]
