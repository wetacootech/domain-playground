"""Crea tar.gz per distribuzioni Mac preservando il bit +x sul binario e sul .command.

Uso: python make_mac_tar.py <src_dir> <out_tar_gz>
"""
import os
import sys
import tarfile


EXECUTABLES = {"WeTacoo.Playground.Web", "Avvia Playground.command"}


def main(src: str, out: str) -> None:
    if not os.path.isdir(src):
        raise SystemExit(f"Source directory non trovata: {src}")

    with tarfile.open(out, "w:gz") as tar:
        for root, _, files in os.walk(src):
            for f in files:
                fp = os.path.join(root, f)
                rel = os.path.relpath(fp, src)
                arcname = rel.replace("\\", "/")
                info = tar.gettarinfo(fp, arcname=arcname)
                info.mode = 0o755 if f in EXECUTABLES else 0o644
                info.uid = 0
                info.gid = 0
                info.uname = ""
                info.gname = ""
                with open(fp, "rb") as fh:
                    tar.addfile(info, fh)
    print(f"OK: {out}")


if __name__ == "__main__":
    if len(sys.argv) != 3:
        raise SystemExit(f"Uso: python {sys.argv[0]} <src_dir> <out_tar_gz>")
    main(sys.argv[1], sys.argv[2])
