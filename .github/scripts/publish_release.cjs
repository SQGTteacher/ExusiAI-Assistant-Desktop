const fs = require('node:fs');
const path = require('node:path');
const crypto = require('node:crypto');

// The build job has read-only permissions. Only this main-branch job can publish.
module.exports = async function publish({ github, context, core, directory = 'artifacts/release' }) {
  if (context.ref !== 'refs/heads/main' || !['push', 'workflow_dispatch'].includes(context.eventName)) {
    throw new Error('Releases can only be published from a validated main build');
  }
  const metadata = JSON.parse(fs.readFileSync(path.join(directory, 'release-metadata.json'), 'utf8'));
  const version = '0.1.0-developer-beta';
  const archive = `ExusiAI-Assistant-Desktop-${version}-win-x64.zip`;
  if (metadata.version !== version || metadata.tag !== `v${version}` || metadata.prerelease !== true ||
      metadata.source_commit !== context.sha || metadata.archive !== archive) {
    throw new Error('Release metadata does not match the authorized version and validated commit');
  }
  const body = fs.readFileSync(path.join(directory, 'RELEASE-NOTES.md'), 'utf8');
  if (!body.includes('请勿使用此版本用于生产环境，仅做开发者参考。')) {
    throw new Error('Developer-only notice is missing');
  }
  const bytes = fs.readFileSync(path.join(directory, archive));
  const digest = crypto.createHash('sha256').update(bytes).digest('hex');
  if (digest !== metadata.sha256 || fs.readFileSync(path.join(directory, `${archive}.sha256`), 'utf8').trim() !== `${digest}  ${archive}`) {
    throw new Error('Release archive checksum does not match the validated build');
  }
  const repo = context.repo;
  let release;
  try {
    release = (await github.rest.repos.getReleaseByTag({ ...repo, tag: metadata.tag })).data;
  } catch (error) {
    if (error.status !== 404) throw error;
  }
  if (release && (release.target_commitish !== context.sha || !release.prerelease)) {
    throw new Error('Existing release belongs to another commit or is not a prerelease; refusing to replace it');
  }
  if (release && !release.draft) {
    core.info(`Already published: ${release.html_url}`);
    return;
  }
  // Never move an existing tag. A partially completed draft can be retried at its original commit.
  try {
    const ref = (await github.rest.git.getRef({ ...repo, ref: `tags/${metadata.tag}` })).data;
    if (ref.object.sha !== context.sha) throw new Error('Existing tag points to another commit; refusing to move it');
  } catch (error) {
    if (error.status !== 404) throw error;
  }
  if (!release) {
    release = (await github.rest.repos.createRelease({
      ...repo, tag_name: metadata.tag, target_commitish: context.sha, name: metadata.name,
      body, draft: true, prerelease: true, make_latest: 'false',
    })).data;
  }
  const assets = await github.paginate(github.rest.repos.listReleaseAssets, { ...repo, release_id: release.id, per_page: 100 });
  for (const name of [archive, `${archive}.sha256`, 'BUILD-INFO.json']) {
    // Replacement is confined to this commit's unpublished draft, for interrupted-upload recovery.
    const previous = assets.find(asset => asset.name === name);
    if (previous) await github.rest.repos.deleteReleaseAsset({ ...repo, asset_id: previous.id });
    const data = name === archive ? bytes : fs.readFileSync(path.join(directory, name));
    await github.rest.repos.uploadReleaseAsset({ ...repo, release_id: release.id, name, data,
      headers: { 'content-type': name.endsWith('.zip') ? 'application/zip' : 'text/plain', 'content-length': data.length } });
  }
  const published = (await github.rest.repos.updateRelease({
    ...repo, release_id: release.id, name: metadata.name, body, draft: false, prerelease: true, make_latest: 'false',
  })).data;
  core.info(`Published developer beta: ${published.html_url}`);
};
