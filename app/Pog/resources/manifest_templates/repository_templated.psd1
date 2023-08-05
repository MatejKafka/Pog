@{
	Name = '{{NAME}}'
	Architecture = 'x64'
	Version = '{{TEMPLATE:Version}}'

	Install = @{
		Url = {$V = $this.Version; ''}
		Hash = '{{TEMPLATE:Hash}}'
	}

	Enable = {

	}
}