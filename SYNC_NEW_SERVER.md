# Syncing Files to New Servers

When adding a new server to `servers.json`, existing files won't automatically sync. KTPFileDistributor only distributes files as they're dropped/changed - it doesn't compare destinations.

Use these rsync commands to do an initial sync from the data server.

---

## Prerequisites

1. SSH key authentication configured (same key used by KTPFileDistributor)
2. Run commands as `ftpuser` or with access to the private key at `/var/www/fastdl/.ssh/id_rsa`

---

## Quick Reference

Replace `<HOST>` and `<PORT>` with the target server IP and game port:

```bash
# Full dod folder
rsync -avz --progress \
  -e "ssh -i /var/www/fastdl/.ssh/id_rsa" \
  /var/www/fastdl/dod/ \
  dodserver@<HOST>:/home/dodserver/dod-<PORT>/serverfiles/dod/

# Plugins only
rsync -avz --progress \
  -e "ssh -i /var/www/fastdl/.ssh/id_rsa" \
  /var/www/fastdl/dod/addons/ktpamx/plugins/ \
  dodserver@<HOST>:/home/dodserver/dod-<PORT>/serverfiles/dod/addons/ktpamx/plugins/

# Maps only
rsync -avz --progress \
  -e "ssh -i /var/www/fastdl/.ssh/id_rsa" \
  /var/www/fastdl/dod/maps/ \
  dodserver@<HOST>:/home/dodserver/dod-<PORT>/serverfiles/dod/maps/
```

---

## Active Servers

| Server | IP | Ports |
|--------|----|-------|
| Atlanta Baremetal | 74.91.121.9 | 27015-27019 |
| Dallas | 74.91.126.55 | 27015-27019 |
| Denver | 66.163.114.109 | 27015-27019 |

**Example — sync all 5 instances on Atlanta:**
```bash
for port in 27015 27016 27017 27018 27019; do
  rsync -avz --progress \
    -e "ssh -i /var/www/fastdl/.ssh/id_rsa" \
    /var/www/fastdl/dod/ \
    dodserver@74.91.121.9:/home/dodserver/dod-$port/serverfiles/dod/
done
```

**Example — sync all 5 instances on Dallas:**
```bash
for port in 27015 27016 27017 27018 27019; do
  rsync -avz --progress \
    -e "ssh -i /var/www/fastdl/.ssh/id_rsa" \
    /var/www/fastdl/dod/ \
    dodserver@74.91.126.55:/home/dodserver/dod-$port/serverfiles/dod/
done
```

**Example — sync all 5 instances on Denver:**
```bash
for port in 27015 27016 27017 27018 27019; do
  rsync -avz --progress \
    -e "ssh -i /var/www/fastdl/.ssh/id_rsa" \
    /var/www/fastdl/dod/ \
    dodserver@66.163.114.109:/home/dodserver/dod-$port/serverfiles/dod/
done
```

---

## Dry Run

Add `-n` flag to preview without transferring:

```bash
rsync -avzn --progress \
  -e "ssh -i /var/www/fastdl/.ssh/id_rsa" \
  /var/www/fastdl/dod/ \
  dodserver@74.91.121.9:/home/dodserver/dod-27015/serverfiles/dod/
```

---

## After Sync

1. Add the new server to `/opt/ktp-file-distributor/servers.json`
2. Restart the service: `sudo systemctl restart ktp-file-distributor`
3. Future file drops will automatically distribute to all enabled servers
