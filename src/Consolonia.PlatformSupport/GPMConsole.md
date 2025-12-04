## Installation Requirements

Users need:
```bash
sudo apt-get install gpm libgpm2  # Debian/Ubuntu

sudo systemctl start gpm
sudo systemctl enable gpm
```

## Configuration

After installation, configure GPM by editing `/etc/gpm.conf`:

```bash
sudo nano /etc/gpm.conf
```

Use the following configuration settings:

```
#  /etc/gpm.conf - configuration file for gpm(1)
#
#  If mouse response seems to be to slow, try using
#  responsiveness=15. append can contain any random arguments to be
#  appended to the commandline.
#
#  If you edit this file by hand, please be aware it is sourced by
#  /etc/init.d/gpm and thus all shell meta characters must be
#  protected from evaluation (i.e. by quoting them).
#
#  This file is used by /etc/init.d/gpm and can be modified by
#  "dpkg-reconfigure gpm" or by hand at your option.
#
device=/dev/input/mice
responsiveness=15
repeat_type=none
type=imps2
append='-a 3 '
sample_rate=
```

After saving the configuration, restart the GPM service:

```bash
sudo systemctl restart gpm
```

