from dataclasses import dataclass


@dataclass(frozen=True)
class MySqlSettings:
    host: str
    port: int
    database: str
    user: str
    password: str
    charset: str = "utf8mb4"

    @property
    def url(self) -> str:
        return (
            f"mysql+pymysql://{self.user}:{self.password}@"
            f"{self.host}:{self.port}/{self.database}?charset={self.charset}"
        )
