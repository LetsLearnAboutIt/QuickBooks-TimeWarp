import pathlib
import olefile

root = pathlib.Path(__file__).resolve().parent
fn = root / 'Installer' / 'QBFC16_0.msm'
if not fn.exists():
    raise FileNotFoundError(fn)
ole = olefile.OleFileIO(fn)
for s in ole.listdir(streams=True):
    with ole.openstream(s) as st:
        data = st.read(8)
        if data.startswith(b'MSCF'):
            out = root / 'tmp_msm_extract' / 'QBFC16_0.cab'
            out.parent.mkdir(parents=True, exist_ok=True)
            st.seek(0)
            out.write_bytes(st.read())
            print('found cab stream', s)
            print('wrote', out)
            break
else:
    raise RuntimeError('No CAB stream found')
